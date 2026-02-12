#pragma warning disable OPENAI001

using System.Runtime.CompilerServices;
using System.ClientModel.Primitives;
using System.ClientModel;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Core;
using Azure.Identity;
using OpenAI;
using OpenAI.Images;
using OpenAI.Responses;
using WebApp.Models;

using ChatAgentInfo = WebApp.Models.AgentInfo;

namespace WebApp.Services;

public class AgentService : IAgentService, IAsyncDisposable
{
    private readonly AIProjectClient? _projectClient;
    private readonly Dictionary<string, AgentVersion> _agents = new();
    private readonly Dictionary<string, ChatAgentInfo> _agentInfos = new();
    private readonly IConfiguration _configuration;
    private readonly ILogger<AgentService> _logger;
    private readonly IWebHostEnvironment _environment;
    private readonly IHttpClientFactory _httpClientFactory;
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public AgentService(
        IConfiguration configuration,
        ILogger<AgentService> logger,
        IWebHostEnvironment environment,
        IHttpClientFactory httpClientFactory)
    {
        _configuration = configuration;
        _logger = logger;
        _environment = environment;
        _httpClientFactory = httpClientFactory;

        var endpoint = GetStringConfig(configuration, "AZURE_PROJECT_ENDPOINT", "Azure:ProjectEndpoint");
        if (string.IsNullOrEmpty(endpoint))
        {
            _logger.LogWarning("Azure:ProjectEndpoint not configured. Agent features will be disabled.");
            return;
        }

        TokenCredential credential = CreateTokenCredential(configuration, logger);

        var imageDeployment = GetImageGenerationDeploymentName();
        if (!string.IsNullOrWhiteSpace(imageDeployment))
        {
            // Per Foundry docs, image generation requires the header:
            // x-ms-oai-image-generation-deployment: <image model deployment>
            AIProjectClientOptions projectOptions = new();
            projectOptions.AddPolicy(new ImageGenerationHeaderPolicy(imageDeployment), PipelinePosition.PerCall);
            _projectClient = new AIProjectClient(new Uri(endpoint), credential, projectOptions);
        }
        else
        {
            _projectClient = new AIProjectClient(new Uri(endpoint), credential);
        }
    }

    private static TokenCredential CreateTokenCredential(IConfiguration configuration, ILogger logger)
    {
        // In Azure App Service, prefer Managed Identity explicitly.
        // WEBSITE_INSTANCE_ID is a reliable signal for App Service environments.
        var isAppService = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID"));

        // Support user-assigned managed identity via client id.
        var managedIdentityClientId = GetStringConfig(configuration, "AZURE_MANAGED_IDENTITY_CLIENT_ID", "Azure:ManagedIdentityClientId")
            ?? Environment.GetEnvironmentVariable("AZURE_CLIENT_ID");

        if (isAppService)
        {
            logger.LogInformation("Using ManagedIdentityCredential for Azure App Service.");
            return string.IsNullOrWhiteSpace(managedIdentityClientId)
                ? new ManagedIdentityCredential()
                : new ManagedIdentityCredential(managedIdentityClientId);
        }

        // Local/dev: use an explicit chain (no DefaultAzureCredential).
        // - VisualStudioCredential works in local dev on Windows when signed in.
        // - AzureCliCredential works when `az login` is available.
        // - EnvironmentCredential supports service-principal env vars (AZURE_TENANT_ID/AZURE_CLIENT_ID/AZURE_CLIENT_SECRET).
        return new ChainedTokenCredential(
            new VisualStudioCredential(),
            new AzureCliCredential(),
            new EnvironmentCredential());
    }

    private string? GetImageGenerationDeploymentName()
    {
        // Prefer the short name requested for this app; allow the longer key for compatibility.
        return GetStringConfig(_configuration, "AZURE_IMAGE_MODEL_DEPLOYMENT_NAME", "Azure:ImageModelDeploymentName")
            ?? _configuration["Azure:ImageGenerationModelDeploymentName"];
    }

    private string? GetVideoGenerationDeploymentName()
        => GetStringConfig(_configuration, "AZURE_VIDEO_MODEL_DEPLOYMENT_NAME", "Azure:VideoModelDeploymentName")
            ?? _configuration["Azure:VideoGenerationModelDeploymentName"];

    private bool IsImageGenerationConfigured()
        => !string.IsNullOrWhiteSpace(GetImageGenerationDeploymentName());

    private bool IsVideoGenerationConfigured()
        => !string.IsNullOrWhiteSpace(GetVideoGenerationDeploymentName());

    private bool IsAzureOpenAiConfigured()
        => !string.IsNullOrWhiteSpace(GetStringConfig(_configuration, "AZURE_OPENAI_ENDPOINT", "AzureOpenAI:Endpoint"))
           && !string.IsNullOrWhiteSpace(GetAzureOpenAiApiKey());

    private string? GetAzureOpenAiApiKey()
    {
        // Prefer env var to keep secrets out of appsettings.
        return Environment.GetEnvironmentVariable("AZURE_API_KEY")
            ?? _configuration["AzureOpenAI:ApiKey"];
    }

    private HttpClient CreateAzureOpenAiClient()
    {
        var endpoint = GetStringConfig(_configuration, "AZURE_OPENAI_ENDPOINT", "AzureOpenAI:Endpoint");
        if (string.IsNullOrWhiteSpace(endpoint))
        {
            throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT (or AzureOpenAI:Endpoint) is required for REST-based image/video generation.");
        }

        var apiKey = GetAzureOpenAiApiKey();
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("AZURE_API_KEY environment variable (or AzureOpenAI:ApiKey) is required for REST-based image/video generation.");
        }

        var client = _httpClientFactory.CreateClient();
        client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
        client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", apiKey);
        return client;
    }

    public async Task InitializeAsync()
    {
        if (_initialized || _projectClient == null) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var deployment = GetStringConfig(_configuration, "AZURE_MODEL_DEPLOYMENT_NAME", "Azure:ModelDeploymentName")
                ?? throw new InvalidOperationException("AZURE_MODEL_DEPLOYMENT_NAME (or Azure:ModelDeploymentName) is required");

            // Create Writer Agent
            _agents["Writer"] = await CreateAgentAsync(deployment, "ChatWriter", """
                You are an excellent content writer.You create new content and
                edit contents based on feedback.Format your responses in Markdown.
                Use code blocks with language identifiers for any code snippets.
                """);
            _agentInfos["Writer"] = new ChatAgentInfo
            {
                Name = "Writer",
                Description = "Creates and edits content",
                Avatar = "W",
                Capabilities = ["Content creation", "Editing", "Markdown formatting"],
                HasTools = false
            };

            // Create Reviewer Agent
            _agents["Reviewer"] = await CreateAgentAsync(deployment, "ChatReviewer", """
                You are an excellent content reviewer.Provide actionable feedback
                in a constructive manner.Use Markdown formatting for structure.
               Be specific about what works well and what could be improved.

               """);

           _agentInfos["Reviewer"] = new ChatAgentInfo
           {
               Name = "Reviewer",
               Description = "Reviews and provides feedback",
               Avatar = "R",
               Capabilities = ["Content review", "Feedback", "Quality analysis"],
               HasTools = false
           };

            // Create Code Assistant
            _agents["Code"] = await CreateAgentAsync(deployment, "ChatCode", """
                You are a helpful code assistant.
                You help with programming questions, code review, debugging, and explaining code.
                Format responses in Markdown.Use fenced code blocks with language identifiers.
                If GitHub MCP tools are available, you may use them to search code, issues, and PRs.
                Be concise but thorough.
                """);
            _agentInfos["Code"] = new ChatAgentInfo
            {
                Name = "Code",
                Description = "Coding + GitHub tools (MCP)",
                Avatar = "C",
                Capabilities = ["Code review", "Debugging", "Explanations", "Best practices", "GitHub MCP tools"],
                HasTools = true
            };

            // Create Image agent (only when configured)
            var imageDeployment = GetImageGenerationDeploymentName();
            if (!string.IsNullOrWhiteSpace(imageDeployment))
            {
                _agentInfos["Image"] = new ChatAgentInfo
                {
                    Name = "Image",
                    Description = "Generates images",
                    Avatar = "I",
                    Capabilities = ["Image generation", "gpt-image-1"],
                    HasTools = true
                };
            }

            if (IsVideoGenerationConfigured())
            {
                _agentInfos["Video"] = new ChatAgentInfo
                {
                    Name = "Video",
                    Description = "Generates videos",
                    Avatar = "V",
                    Capabilities = ["Video generation (REST)", "Sora"],
                    HasTools = true
                };
            }

            // Best practice: configure MCP tools in the Foundry project/agent tool catalog,
            // then attach the MCP tool per request (as this app does for the GitHub agent).
            // Avoid creating agent versions at runtime just to "register" an MCP server.

            _initialized = true;
            _logger.LogInformation("Initialized {Count} agents", _agents.Count);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async IAsyncEnumerable<AgentStreamUpdate> GetStreamingResponseAsync(
        string agentName,
        string prompt,
        IEnumerable<ChatMessage> history,
        IReadOnlyList<string>? imagePaths = null,
        string? githubToken = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await InitializeAsync();

        // If images are attached, use a multimodal REST call for text agents.
        // (Media generation agents remain REST-based but do not currently accept image inputs.)
        if (imagePaths is { Count: > 0 }
            && agentName is not ("Image" or "Video"))
        {
            var azureOpenAiEndpoint = GetStringConfig(_configuration, "AZURE_OPENAI_ENDPOINT", "AzureOpenAI:Endpoint");
            var azureOpenAiApiKey = GetAzureOpenAiApiKey();
            if (string.IsNullOrWhiteSpace(azureOpenAiEndpoint) || string.IsNullOrWhiteSpace(azureOpenAiApiKey))
            {
                // Don't hard-fail the user message. Continue text-only and explain what's missing.
                if (string.IsNullOrWhiteSpace(azureOpenAiEndpoint) && string.IsNullOrWhiteSpace(azureOpenAiApiKey))
                {
                    yield return new TextDeltaUpdate("Image understanding is not configured (missing AZURE_OPENAI_ENDPOINT and AZURE_API_KEY). Continuing without image analysis.\n\n");
                }
                else if (string.IsNullOrWhiteSpace(azureOpenAiEndpoint))
                {
                    yield return new TextDeltaUpdate("Image understanding is not configured (missing AZURE_OPENAI_ENDPOINT). Continuing without image analysis.\n\n");
                }
                else
                {
                    yield return new TextDeltaUpdate("Image understanding is not configured (missing AZURE_API_KEY). Continuing without image analysis.\n\n");
                }
            }
            else
            {
                var loadedImages = TryLoadUploadedImages(imagePaths);
                if (loadedImages.Count == 0)
                {
                    yield return new TextDeltaUpdate("No valid image attachments were found. Continuing without image analysis.\n\n");
                }
                else
                {
                    yield return new TextDeltaUpdate("Analyzing image(s)...\n");

                    string? text = null;
                    string? multimodalError = null;
                    try
                    {
                        text = await GetTextResponseViaRestWithImagesAsync(prompt, history, loadedImages, cancellationToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Multimodal REST call failed");
                        multimodalError = ex.Message;
                    }

                    if (!string.IsNullOrWhiteSpace(multimodalError))
                    {
                        yield return new TextDeltaUpdate($"Image understanding failed: {multimodalError}");
                        yield break;
                    }

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        yield return new TextDeltaUpdate("The model returned an empty response.");
                        yield break;
                    }

                    yield return new TextDeltaUpdate(text);
                    yield break;
                }
            }
        }

        // REST-based media agents do not require an Azure AI Foundry project client.
        if (agentName == "Image")
        {
            var imageDeployment = GetImageGenerationDeploymentName();
            if (string.IsNullOrWhiteSpace(imageDeployment))
            {
                yield return new TextDeltaUpdate("Image generation isn't configured. Set Azure:ImageModelDeploymentName to your image model name/deployment (e.g., 'dall-e-3').");
                yield break;
            }

            if (!IsAzureOpenAiConfigured())
            {
                yield return new TextDeltaUpdate("Image generation requires Azure OpenAI REST config. Set AZURE_OPENAI_ENDPOINT (or AzureOpenAI:Endpoint) and set AZURE_API_KEY.");
                yield break;
            }

            yield return new TextDeltaUpdate("Generating image...\n");

            byte[]? pngBytes = null;
            string? error = null;

            try
            {
                pngBytes = await GenerateImageViaSdkAsync(prompt, imageDeployment, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Image generation call failed");
                error = ex.Message;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                yield return new TextDeltaUpdate($"Image generation failed: {error}");
                yield break;
            }

            if (pngBytes is not { Length: > 0 })
            {
                yield return new TextDeltaUpdate("Image generation completed but no image payload was returned.");
                yield break;
            }

            var publicPath = SaveGeneratedImage(pngBytes);
            yield return new TextDeltaUpdate($"![Generated image]({publicPath})");
            yield break;
        }

        if (agentName == "Video")
        {
            var videoDeployment = GetVideoGenerationDeploymentName();
            if (string.IsNullOrWhiteSpace(videoDeployment))
            {
                yield return new TextDeltaUpdate("Video generation isn't configured. Set Azure:VideoModelDeploymentName to your video model deployment name (e.g., 'sora').");
                yield break;
            }

            if (!IsAzureOpenAiConfigured())
            {
                yield return new TextDeltaUpdate("Video generation requires Azure OpenAI REST config. Set AZURE_OPENAI_ENDPOINT (or AzureOpenAI:Endpoint) and set AZURE_API_KEY.");
                yield break;
            }

            yield return new TextDeltaUpdate("Starting video generation job...\n");

            VideoJobResult? result = null;
            string? statusJson = null;
            string? error = null;

            try
            {
                result = await GenerateVideoViaRestAsync(prompt, videoDeployment, cancellationToken);
                statusJson = result.StatusJson;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Video generation REST call failed");
                error = ex.Message;
            }

            if (!string.IsNullOrWhiteSpace(error))
            {
                yield return new TextDeltaUpdate($"Video generation failed: {error}");
                yield break;
            }

            if (result?.VideoBytes is { Length: > 0 })
            {
                var publicPath = SaveGeneratedVideo(result.VideoBytes, result.FileExtension ?? ".mp4");
                yield return new TextDeltaUpdate($"<video controls style=\"max-width: 100%;\" src=\"{publicPath}\"></video>\n\n[Download video]({publicPath})");
                yield break;
            }

            if (!string.IsNullOrWhiteSpace(result?.VideoUrl))
            {
                yield return new TextDeltaUpdate($"Video is ready: {result!.VideoUrl}");
                yield break;
            }

            yield return new TextDeltaUpdate("Video generation completed but no video payload/url was detected. Raw status:\n\n```json\n" + (statusJson ?? "(none)") + "\n```");
            yield break;
        }

        // From here down, agents require the Foundry project client.
        if (_projectClient == null)
        {
            yield return new TextDeltaUpdate("Agent service is not configured. Please set Azure:ProjectEndpoint in configuration.");
            yield break;
        }

        // Code agent: optionally attaches GitHub MCP tool per request when the user is authenticated.
        // Keep "GitHub" as a compatibility alias (OAuth redirect/querystring) that requires auth.
        if (agentName is "Code" or "GitHub")
        {
            var deployment = GetStringConfig(_configuration, "AZURE_MODEL_DEPLOYMENT_NAME", "Azure:ModelDeploymentName")
                ?? throw new InvalidOperationException("AZURE_MODEL_DEPLOYMENT_NAME (or Azure:ModelDeploymentName) is required");
            var mcpUrl = GetStringConfig(_configuration, "GITHUB_MCP_SERVER_URL", "Azure:GitHubMcpServerUrl");

            var requiresAuth = agentName == "GitHub";
            if (requiresAuth && string.IsNullOrEmpty(githubToken))
            {
                yield return new TextDeltaUpdate("Please sign in with GitHub first to use GitHub MCP tools.");
                yield break;
            }

            // Only attach MCP tools when we have a token + MCP server URL.
            if (!string.IsNullOrEmpty(githubToken) && !string.IsNullOrEmpty(mcpUrl))
            {
                var responsesClient = _projectClient.OpenAI.GetProjectResponsesClientForModel(deployment);

                var mcpOptions = new CreateResponseOptions
                {
                    Instructions = """
                        You are a helpful software engineering assistant.
                        Format responses in Markdown and use fenced code blocks with language identifiers.
                        You have access to GitHub via MCP when needed; prefer using tools for repo / issue / code facts.
                        Be concise but thorough.
                        """
                };

            foreach (var msg in history.TakeLast(20))
            {
                mcpOptions.InputItems.Add(msg.Role == "user"
                    ? ResponseItem.CreateUserMessageItem(msg.Content)
                    : ResponseItem.CreateAssistantMessageItem(msg.Content));
            }
            mcpOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

            var mcpTool = ResponseTool.CreateMcpTool(
                serverLabel: "github",
                serverUri: new Uri(mcpUrl),
                authorizationToken: githubToken,
                toolCallApprovalPolicy: new McpToolCallApprovalPolicy(
                    GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval));
            mcpOptions.Tools.Add(mcpTool);

            await foreach (var update in HandleMcpStreamingAsync(responsesClient, mcpOptions, cancellationToken))
            {
                yield return update;
            }
            yield break;
        }

        // No MCP available; proceed with normal agent flow (streaming) below.
    }

        if (!_agents.TryGetValue(agentName, out var agent))
        {
            yield return new TextDeltaUpdate($"Unknown agent: {agentName}. Available agents: {string.Join(", ", _agents.Keys)}");
    yield break;
        }

var responseClient = _projectClient.OpenAI.GetProjectResponsesClientForAgent(agent.Name);

// Build conversation context
var options = new CreateResponseOptions
{
    StreamingEnabled = true
};
        foreach (var msg in history.TakeLast(20))
        {
            options.InputItems.Add(msg.Role == "user"
                ? ResponseItem.CreateUserMessageItem(msg.Content)
                : ResponseItem.CreateAssistantMessageItem(msg.Content));
        }
        options.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

await foreach (var update in responseClient.CreateResponseStreamingAsync(options, cancellationToken))
{
    if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
    {
        yield return new TextDeltaUpdate(textDelta.Delta);
    }
}
    }

    private sealed record UploadedImage(string ContentType, byte[] Bytes);

private List<UploadedImage> TryLoadUploadedImages(IReadOnlyList<string> imagePaths)
{
    var result = new List<UploadedImage>();
    if (string.IsNullOrWhiteSpace(_environment.WebRootPath))
    {
        return result;
    }

    foreach (var raw in imagePaths)
    {
        if (string.IsNullOrWhiteSpace(raw)) continue;

        // Expect paths like /uploads/<file>
        var normalized = raw.Trim();
        if (!normalized.StartsWith("/uploads/", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        var relative = normalized.TrimStart('/').Replace('/', Path.DirectorySeparatorChar);
        var fullPath = Path.GetFullPath(Path.Combine(_environment.WebRootPath, relative));

        // Prevent path traversal: ensure the file stays under wwwroot/uploads
        var uploadsRoot = Path.GetFullPath(Path.Combine(_environment.WebRootPath, "uploads" + Path.DirectorySeparatorChar));
        if (!fullPath.StartsWith(uploadsRoot, StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (!File.Exists(fullPath)) continue;

        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        var contentType = ext switch
        {
            ".png" => "image/png",
            ".jpg" or ".jpeg" => "image/jpeg",
            ".webp" => "image/webp",
            ".gif" => "image/gif",
            _ => ""
        };
        if (string.IsNullOrWhiteSpace(contentType)) continue;

        try
        {
            var bytes = File.ReadAllBytes(fullPath);
            if (bytes.Length == 0) continue;

            // Cap at 5MB per image to keep requests reasonable.
            if (bytes.Length > 5 * 1024 * 1024) continue;

            result.Add(new UploadedImage(contentType, bytes));
        }
        catch
        {
            // Ignore bad files
        }
    }

    // Keep it small.
    if (result.Count > 5)
    {
        result = result.Take(5).ToList();
    }

    return result;
}

private string? GetTextModelDeploymentName()
    => GetStringConfig(_configuration, "AZURE_MODEL_DEPLOYMENT_NAME", "Azure:ModelDeploymentName");

private async Task<string> GetTextResponseViaRestWithImagesAsync(
    string prompt,
    IEnumerable<ChatMessage> history,
    List<UploadedImage> images,
    CancellationToken cancellationToken)
{
    var model = GetTextModelDeploymentName();
    if (string.IsNullOrWhiteSpace(model))
    {
        throw new InvalidOperationException("AZURE_MODEL_DEPLOYMENT_NAME (or Azure:ModelDeploymentName) is required for text responses.");
    }

    var input = new List<object>();
    foreach (var msg in history.TakeLast(20))
    {
        input.Add(new
        {
            role = msg.Role,
            content = new object[]
            {
                    new { type = "input_text", text = msg.Content }
            }
        });
    }

    var contentParts = new List<object>
        {
            new { type = "input_text", text = prompt }
        };

    foreach (var img in images)
    {
        var b64 = Convert.ToBase64String(img.Bytes);
        var dataUrl = $"data:{img.ContentType};base64,{b64}";
        contentParts.Add(new { type = "input_image", image_url = dataUrl });
    }

    input.Add(new { role = "user", content = contentParts.ToArray() });

    var body = new
    {
        model,
        input
    };

    var client = CreateAzureOpenAiClient();
    using var resp = await client.PostAsJsonAsync("openai/v1/responses", body, cancellationToken);
    var json = await resp.Content.ReadAsStringAsync(cancellationToken);
    if (!resp.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Azure OpenAI responses call failed ({(int)resp.StatusCode}): {json}");
    }

    using var doc = System.Text.Json.JsonDocument.Parse(json);
    if (TryExtractResponsesOutputText(doc.RootElement, out var text))
    {
        return text;
    }

    return json;
}

private static bool TryExtractResponsesOutputText(System.Text.Json.JsonElement root, out string text)
{
    text = string.Empty;

    if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
    {
        return false;
    }

    if (root.TryGetProperty("output_text", out var outputTextEl)
        && outputTextEl.ValueKind == System.Text.Json.JsonValueKind.String)
    {
        var s = outputTextEl.GetString();
        if (!string.IsNullOrWhiteSpace(s))
        {
            text = s;
            return true;
        }
    }

    if (!root.TryGetProperty("output", out var outputEl)
        || outputEl.ValueKind != System.Text.Json.JsonValueKind.Array)
    {
        return false;
    }

    var sb = new System.Text.StringBuilder();
    foreach (var item in outputEl.EnumerateArray())
    {
        if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
        if (!item.TryGetProperty("content", out var contentEl)
            || contentEl.ValueKind != System.Text.Json.JsonValueKind.Array)
        {
            continue;
        }

        foreach (var part in contentEl.EnumerateArray())
        {
            if (part.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
            var type = part.TryGetProperty("type", out var t) ? t.GetString() : null;
            if (!string.Equals(type, "output_text", StringComparison.OrdinalIgnoreCase)) continue;
            var partText = part.TryGetProperty("text", out var pt) ? pt.GetString() : null;
            if (!string.IsNullOrWhiteSpace(partText))
            {
                sb.Append(partText);
            }
        }
    }

    if (sb.Length == 0) return false;
    text = sb.ToString();
    return true;
}

private string SaveGeneratedImage(byte[] pngBytes)
{
    var folder = Path.Combine(_environment.WebRootPath, "generated");
    Directory.CreateDirectory(folder);

    var fileName = $"{Guid.NewGuid():N}.png";
    var filePath = Path.Combine(folder, fileName);
    File.WriteAllBytes(filePath, pngBytes);

    return $"/generated/{fileName}";
}

private string SaveGeneratedVideo(byte[] bytes, string fileExtension)
{
    var folder = Path.Combine(_environment.WebRootPath, "generated");
    Directory.CreateDirectory(folder);

    var ext = string.IsNullOrWhiteSpace(fileExtension) ? ".mp4" : fileExtension;
    if (!ext.StartsWith('.')) ext = "." + ext;

    var fileName = $"{Guid.NewGuid():N}{ext}";
    var filePath = Path.Combine(folder, fileName);
    File.WriteAllBytes(filePath, bytes);

    return $"/generated/{fileName}";
}

private ImageClient CreateAzureOpenAiImageClient(string deploymentName)
{
    var endpoint = GetStringConfig(_configuration, "AZURE_OPENAI_ENDPOINT", "AzureOpenAI:Endpoint");
    if (string.IsNullOrWhiteSpace(endpoint))
    {
        throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT (or AzureOpenAI:Endpoint) is required for image generation.");
    }

    var apiKey = GetAzureOpenAiApiKey();
    if (string.IsNullOrWhiteSpace(apiKey))
    {
        throw new InvalidOperationException("AZURE_API_KEY environment variable (or AzureOpenAI:ApiKey) is required for image generation.");
    }

    // The OpenAI .NET ImageClient expects the Azure OpenAI "openai/v1" endpoint.
    // Accept either the base resource endpoint (https://{name}.openai.azure.com)
    // or the full endpoint (https://{name}.openai.azure.com/openai/v1/).
    var normalizedEndpoint = endpoint.TrimEnd('/');
    if (!normalizedEndpoint.EndsWith("/openai/v1", StringComparison.OrdinalIgnoreCase))
    {
        normalizedEndpoint += "/openai/v1";
    }

    return new ImageClient(
        credential: new ApiKeyCredential(apiKey),
        model: deploymentName,
        options: new OpenAIClientOptions
        {
            Endpoint = new Uri(normalizedEndpoint + "/")
        }
    );
}

private static GeneratedImageSize? TryMapGeneratedImageSize(string? size)
{
    if (string.IsNullOrWhiteSpace(size)) return null;

    return size.Trim() switch
    {
        "1024x1024" => GeneratedImageSize.W1024xH1024,
        "1024x1792" => GeneratedImageSize.W1024xH1792,
        "1792x1024" => GeneratedImageSize.W1792xH1024,
        _ => null
    };
}

private async Task<byte[]> GenerateImageViaSdkAsync(string prompt, string deploymentName, CancellationToken cancellationToken)
{
    var client = CreateAzureOpenAiImageClient(deploymentName);

    ImageGenerationOptions options = new();
    var size = TryMapGeneratedImageSize(GetStringConfig(_configuration, "AZURE_OPENAI_IMAGE_SIZE", "AzureOpenAI:Images:Size"));
    if (size is not null)
    {
        options.Size = size.Value;
    }

    // Matches the user's sample: Generate image and return raw bytes.
    GeneratedImage image = await client.GenerateImageAsync(prompt, options, cancellationToken);
    return image.ImageBytes.ToArray();
}

private sealed record VideoJobResult(string? StatusJson, byte[]? VideoBytes, string? FileExtension, string? VideoUrl);

private async Task<VideoJobResult> GenerateVideoViaRestAsync(string prompt, string model, CancellationToken cancellationToken)
{
    var client = CreateAzureOpenAiClient();

    // Matches the user's Azure OpenAI REST sample:
    // POST https://{resource}.openai.azure.com/openai/v1/videos
    // { prompt, size: "720x1280", seconds: 4, model: "sora-2" }
    var height = GetIntConfig(_configuration, "AZURE_OPENAI_VIDEO_HEIGHT", "AzureOpenAI:Video:Height") ?? 1080;
    var width = GetIntConfig(_configuration, "AZURE_OPENAI_VIDEO_WIDTH", "AzureOpenAI:Video:Width") ?? 1080;
    var size = GetStringConfig(_configuration, "AZURE_OPENAI_VIDEO_SIZE", "AzureOpenAI:Video:Size") ?? $"{height}x{width}";
    var seconds = GetIntConfig(_configuration, "AZURE_OPENAI_VIDEO_SECONDS", "AzureOpenAI:Video:Seconds") ?? 4;
    if (seconds is not (4 or 8 or 12))
    {
        throw new InvalidOperationException("AZURE_OPENAI_VIDEO_SECONDS (or AzureOpenAI:Video:Seconds) must be one of 4, 8, or 12 for the /openai/v1/videos endpoint.");
    }

    var body = new
    {
        prompt,
        size,
        seconds = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture),
        model
    };

    using var submit = await client.PostAsJsonAsync("openai/v1/videos", body, cancellationToken);
    var submitJson = await submit.Content.ReadAsStringAsync(cancellationToken);
    if (!submit.IsSuccessStatusCode)
    {
        throw new InvalidOperationException($"Azure OpenAI video job submit failed ({(int)submit.StatusCode}): {submitJson}");
    }

    using var submitDoc = System.Text.Json.JsonDocument.Parse(submitJson);

    // Some services respond synchronously with a URL or base64 payload.
    if (TryExtractVideoUrlOrBytes(submitDoc.RootElement, out var immediateUrl, out var immediateBytes, out var immediateExt))
    {
        return new VideoJobResult(StatusJson: submitJson, VideoBytes: immediateBytes, FileExtension: immediateExt, VideoUrl: immediateUrl);
    }

    var jobId = submitDoc.RootElement.TryGetProperty("id", out var idEl)
        ? idEl.GetString()
        : null;

    if (string.IsNullOrWhiteSpace(jobId))
    {
        // Some services may return jobId or job_id
        if (submitDoc.RootElement.TryGetProperty("job_id", out var jidEl))
        {
            jobId = jidEl.GetString();
        }
        else if (submitDoc.RootElement.TryGetProperty("jobId", out var jIdEl))
        {
            jobId = jIdEl.GetString();
        }
    }

    if (string.IsNullOrWhiteSpace(jobId))
    {
        return new VideoJobResult(StatusJson: submitJson, VideoBytes: null, FileExtension: null, VideoUrl: null);
    }

    var pollIntervalMs = GetIntConfig(_configuration, "AZURE_OPENAI_VIDEO_POLL_INTERVAL_MS", "AzureOpenAI:Video:PollIntervalMs") ?? 1500;
    var maxAttempts = GetIntConfig(_configuration, "AZURE_OPENAI_VIDEO_MAX_POLL_ATTEMPTS", "AzureOpenAI:Video:MaxPollAttempts") ?? 60;

    string? lastJson = null;
    for (var attempt = 0; attempt < maxAttempts; attempt++)
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Delay(pollIntervalMs, cancellationToken);

        using var status = await client.GetAsync($"openai/v1/videos/{Uri.EscapeDataString(jobId)}", cancellationToken);
        lastJson = await status.Content.ReadAsStringAsync(cancellationToken);
        if (!status.IsSuccessStatusCode)
        {
            // Keep polling transient failures a couple times.
            continue;
        }

        using var doc = System.Text.Json.JsonDocument.Parse(lastJson);
        var root = doc.RootElement;

        var statusStr = root.TryGetProperty("status", out var statusEl)
            ? statusEl.GetString()
            : null;

        if (string.Equals(statusStr, "failed", StringComparison.OrdinalIgnoreCase))
        {
            return new VideoJobResult(StatusJson: lastJson, VideoBytes: null, FileExtension: null, VideoUrl: null);
        }

        if (!string.Equals(statusStr, "succeeded", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(statusStr, "completed", StringComparison.OrdinalIgnoreCase))
        {
            continue;
        }

        if (TryExtractVideoUrlOrBytes(root, out var url, out var bytes, out var ext))
        {
            if (bytes is { Length: > 0 })
            {
                return new VideoJobResult(StatusJson: lastJson, VideoBytes: bytes, FileExtension: ext ?? ".mp4", VideoUrl: url);
            }

            if (!string.IsNullOrWhiteSpace(url))
            {
                // If it's a URL, optionally try downloading bytes.
                try
                {
                    using var download = await client.GetAsync(url, cancellationToken);
                    if (download.IsSuccessStatusCode)
                    {
                        var downloadedBytes = await download.Content.ReadAsByteArrayAsync(cancellationToken);
                        if (downloadedBytes.Length > 0)
                        {
                            var downloadedExt = download.Content.Headers.ContentType?.MediaType == "video/mp4" ? ".mp4" : ".bin";
                            return new VideoJobResult(StatusJson: lastJson, VideoBytes: downloadedBytes, FileExtension: downloadedExt, VideoUrl: url);
                        }
                    }
                }
                catch
                {
                    // Ignore download failure; still return URL.
                }

                return new VideoJobResult(StatusJson: lastJson, VideoBytes: null, FileExtension: null, VideoUrl: url);
            }
        }

        // Some APIs return a completed job record with no output fields.
        // Attempt common follow-up endpoints to fetch the generated bytes.
        if (!string.IsNullOrWhiteSpace(jobId))
        {
            var fetched = await TryFetchVideoContentAsync(client, jobId, cancellationToken);
            if (fetched is not null)
            {
                return new VideoJobResult(StatusJson: lastJson, VideoBytes: fetched.Value.Bytes, FileExtension: fetched.Value.FileExtension, VideoUrl: fetched.Value.Url);
            }
        }

        return new VideoJobResult(StatusJson: lastJson, VideoBytes: null, FileExtension: null, VideoUrl: null);
    }

    return new VideoJobResult(StatusJson: lastJson ?? submitJson, VideoBytes: null, FileExtension: null, VideoUrl: null);
}

private static string? GetStringConfig(IConfiguration configuration, string flatKey, string legacyKey)
    => configuration[flatKey] ?? configuration[legacyKey];

private static int? GetIntConfig(IConfiguration configuration, string flatKey, string legacyKey)
{
    var flat = configuration[flatKey];
    if (!string.IsNullOrWhiteSpace(flat)
        && int.TryParse(flat, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var flatValue))
    {
        return flatValue;
    }

    return configuration.GetValue<int?>(legacyKey);
}

private readonly record struct FetchedVideo(string? Url, byte[]? Bytes, string FileExtension);

private static async Task<FetchedVideo?> TryFetchVideoContentAsync(HttpClient client, string videoId, CancellationToken cancellationToken)
{
    // Common patterns seen in async media APIs: either a direct content endpoint or a JSON wrapper with a URL.
    // We try a small set of likely endpoints and accept either video bytes or a url payload.
    string[] candidates =

    [
        $"openai/v1/videos/{Uri.EscapeDataString(videoId)}/content",
        $"openai/v1/videos/{Uri.EscapeDataString(videoId)}/result",
        $"openai/v1/videos/{Uri.EscapeDataString(videoId)}/download",
        $"openai/v1/videos/{Uri.EscapeDataString(videoId)}/file"
    ];

    foreach (var path in candidates)
    {
        try
        {
            using var resp = await client.GetAsync(path, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                continue;
            }

            var mediaType = resp.Content.Headers.ContentType?.MediaType;
            if (!string.IsNullOrWhiteSpace(mediaType) && mediaType.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (TryExtractVideoUrlOrBytes(doc.RootElement, out var url, out var bytes, out var ext))
                {
                    if (bytes is { Length: > 0 })
                    {
                        return new FetchedVideo(url, bytes, ext ?? ".mp4");
                    }

                    if (!string.IsNullOrWhiteSpace(url))
                    {
                        return new FetchedVideo(url, null, ".mp4");
                    }
                }

                continue;
            }

            var contentBytes = await resp.Content.ReadAsByteArrayAsync(cancellationToken);
            if (contentBytes is { Length: > 0 })
            {
                var ext = mediaType is not null && mediaType.Contains("mp4", StringComparison.OrdinalIgnoreCase)
                    ? ".mp4"
                    : mediaType is not null && mediaType.Contains("webm", StringComparison.OrdinalIgnoreCase)
                        ? ".webm"
                        : ".bin";
                return new FetchedVideo(null, contentBytes, ext);
            }
        }
        catch
        {
            // Ignore and continue to next candidate.
        }
    }

    return null;
}

private static bool TryExtractVideoUrlOrBytes(System.Text.Json.JsonElement root, out string? url, out byte[]? bytes, out string? fileExtension)
{
    url = null;
    bytes = null;
    fileExtension = null;

    if (root.ValueKind != System.Text.Json.JsonValueKind.Object)
    {
        return false;
    }

    if (root.TryGetProperty("url", out var urlEl)) url = urlEl.GetString();
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("video_url", out var vUrlEl)) url = vUrlEl.GetString();
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("content_url", out var cUrlEl)) url = cUrlEl.GetString();
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("download_url", out var dUrlEl)) url = dUrlEl.GetString();
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("result_url", out var rUrlEl)) url = rUrlEl.GetString();
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("contentUrl", out var cUrlEl2)) url = cUrlEl2.GetString();
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("downloadUrl", out var dUrlEl2)) url = dUrlEl2.GetString();
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("fileUrl", out var fUrlEl2)) url = fUrlEl2.GetString();
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("videoUrl", out var vUrlEl2)) url = vUrlEl2.GetString();

    // data[0].url or data[0].b64_json
    if (root.TryGetProperty("data", out var dataEl)
        && dataEl.ValueKind == System.Text.Json.JsonValueKind.Array
        && dataEl.GetArrayLength() > 0)
    {
        var first = dataEl[0];
        if (string.IsNullOrWhiteSpace(url) && first.TryGetProperty("url", out var du)) url = du.GetString();

        if (first.TryGetProperty("b64_json", out var b64El))
        {
            var b64 = b64El.GetString();
            if (!string.IsNullOrWhiteSpace(b64))
            {
                bytes = Convert.FromBase64String(b64);
                fileExtension = ".mp4";
                return true;
            }
        }
    }

    // result.url or output.url
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("result", out var resultEl) && resultEl.ValueKind == System.Text.Json.JsonValueKind.Object)
    {
        if (resultEl.TryGetProperty("url", out var ru)) url = ru.GetString();
        if (string.IsNullOrWhiteSpace(url) && resultEl.TryGetProperty("content_url", out var rcu)) url = rcu.GetString();
        if (string.IsNullOrWhiteSpace(url) && resultEl.TryGetProperty("download_url", out var rdu)) url = rdu.GetString();
        if (string.IsNullOrWhiteSpace(url) && resultEl.TryGetProperty("contentUrl", out var rcu2)) url = rcu2.GetString();
        if (string.IsNullOrWhiteSpace(url) && resultEl.TryGetProperty("downloadUrl", out var rdu2)) url = rdu2.GetString();
    }
    if (string.IsNullOrWhiteSpace(url) && root.TryGetProperty("output", out var outputEl))
    {
        if (outputEl.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (outputEl.TryGetProperty("url", out var ou)) url = ou.GetString();
            if (string.IsNullOrWhiteSpace(url) && outputEl.TryGetProperty("video_url", out var ovu)) url = ovu.GetString();
            if (string.IsNullOrWhiteSpace(url) && outputEl.TryGetProperty("content_url", out var ocu)) url = ocu.GetString();
            if (string.IsNullOrWhiteSpace(url) && outputEl.TryGetProperty("download_url", out var odu)) url = odu.GetString();
            if (string.IsNullOrWhiteSpace(url) && outputEl.TryGetProperty("contentUrl", out var ocu2)) url = ocu2.GetString();
            if (string.IsNullOrWhiteSpace(url) && outputEl.TryGetProperty("downloadUrl", out var odu2)) url = odu2.GetString();
        }
        else if (outputEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            foreach (var item in outputEl.EnumerateArray())
            {
                if (item.ValueKind != System.Text.Json.JsonValueKind.Object) continue;
                if (TryExtractVideoUrlOrBytes(item, out var iu, out var ib, out var ie))
                {
                    url ??= iu;
                    bytes ??= ib;
                    fileExtension ??= ie;
                    break;
                }
            }
        }
    }

    // Fallback: some APIs bury URLs deeply; scan for the first http(s) string in any `*url*` field.
    if (string.IsNullOrWhiteSpace(url) && TryFindFirstUrl(root, out var anyUrl))
    {
        url = anyUrl;
    }

    return !string.IsNullOrWhiteSpace(url) || (bytes is { Length: > 0 });
}

private static bool TryFindFirstUrl(System.Text.Json.JsonElement element, out string? url)
{
    url = null;

    switch (element.ValueKind)
    {
        case System.Text.Json.JsonValueKind.Object:
            foreach (var prop in element.EnumerateObject())
            {
                if (prop.Value.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    var s = prop.Value.GetString();
                    if (!string.IsNullOrWhiteSpace(s)
                        && (s.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                            || s.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                        && prop.Name.Contains("url", StringComparison.OrdinalIgnoreCase))
                    {
                        url = s;
                        return true;
                    }
                }

                if (TryFindFirstUrl(prop.Value, out url))
                {
                    return true;
                }
            }
            return false;

        case System.Text.Json.JsonValueKind.Array:
            foreach (var item in element.EnumerateArray())
            {
                if (TryFindFirstUrl(item, out url))
                {
                    return true;
                }
            }
            return false;

        default:
            return false;
    }
}

private async IAsyncEnumerable<AgentStreamUpdate> HandleMcpStreamingAsync(
    ProjectResponsesClient responseClient,
    CreateResponseOptions options,
    [EnumeratorCancellation] CancellationToken cancellationToken)
{
    ResponseResult? latestResponse = null;
    CreateResponseOptions? nextOptions = options;

    while (nextOptions != null)
    {
        latestResponse = await responseClient.CreateResponseAsync(nextOptions, cancellationToken);
        nextOptions = null;

        var approvals = latestResponse.OutputItems
            .OfType<McpToolCallApprovalRequestItem>()
            .ToList();

        if (approvals.Count > 0)
        {
            foreach (var approval in approvals)
            {
                yield return new ToolCallUpdate(approval.ServerLabel, "Requesting approval...");
            }

            // Auto-approve for demo purposes.
            // IMPORTANT: carry forward the original Instructions/Tools when continuing a response.
            // Some SDK/service combinations will not execute the tool after approval unless tools are present.
            nextOptions = new CreateResponseOptions
            {
                PreviousResponseId = latestResponse.Id,
                Instructions = options.Instructions,
                StreamingEnabled = options.StreamingEnabled
            };
            foreach (var tool in options.Tools)
            {
                nextOptions.Tools.Add(tool);
            }

            foreach (var approval in approvals)
            {
                nextOptions.InputItems.Add(ResponseItem.CreateMcpApprovalResponseItem(
                    approvalRequestId: approval.Id,
                    approved: true));

                yield return new ToolResultUpdate(approval.ServerLabel, "Approved");
            }
        }
    }

    if (latestResponse != null)
    {
        var outputText = latestResponse.GetOutputText();
        if (!string.IsNullOrEmpty(outputText))
        {
            yield return new TextDeltaUpdate(outputText);
        }
    }
}

public Task<ChatAgentInfo> GetAgentInfoAsync(string agentName)
{
    if (_agentInfos.TryGetValue(agentName, out var info))
    {
        return Task.FromResult(info);
    }

    return Task.FromResult(new ChatAgentInfo
    {
        Name = agentName,
        Description = "Unknown agent",
        Avatar = agentName[0].ToString().ToUpper()
    });
}

public Task<IEnumerable<ChatAgentInfo>> GetAvailableAgentsAsync()
{
    if (_agentInfos.Count == 0)
    {
        // Return default list if not initialized
        var defaults = new List<ChatAgentInfo>
            {
                new ChatAgentInfo { Name = "Writer", Description = "Creates and edits content", Avatar = "W" },
                new ChatAgentInfo { Name = "Reviewer", Description = "Reviews and provides feedback", Avatar = "R" },
                new ChatAgentInfo { Name = "Code", Description = "Coding + GitHub tools (MCP)", Avatar = "C", HasTools = true }
            };

        if (IsImageGenerationConfigured())
        {
            defaults.Add(new ChatAgentInfo { Name = "Image", Description = "Generates images", Avatar = "I", HasTools = true });
        }

        if (IsVideoGenerationConfigured())
        {
            defaults.Add(new ChatAgentInfo { Name = "Video", Description = "Generates videos", Avatar = "V", HasTools = true });
        }

        return Task.FromResult<IEnumerable<ChatAgentInfo>>(defaults);
    }

    return Task.FromResult<IEnumerable<ChatAgentInfo>>(_agentInfos.Values);
}

private async Task<AgentVersion> CreateAgentAsync(string model, string name, string instructions, IEnumerable<ResponseTool>? tools = null)
{
    var definition = new PromptAgentDefinition(model: model)
    {
        Instructions = instructions
    };

    if (tools != null)
    {
        foreach (var tool in tools)
        {
            definition.Tools.Add(tool);
        }
    }

    var result = await _projectClient!.Agents.CreateAgentVersionAsync(
        agentName: name,
        options: new AgentVersionCreationOptions(definition));

    _logger.LogInformation("Created agent: {Name} v{Version}", result.Value.Name, result.Value.Version);
    return result.Value;
}

// Per Foundry docs, image generation requests must include:
// x-ms-oai-image-generation-deployment: <image model deployment name>
internal sealed class ImageGenerationHeaderPolicy(string imageDeployment) : PipelinePolicy
{
        private const string HeaderName = "x-ms-oai-image-generation-deployment";

public override void Process(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
{
    message.Request.Headers.Add(HeaderName, imageDeployment);
    ProcessNext(message, pipeline, currentIndex);
}

public override async ValueTask ProcessAsync(PipelineMessage message, IReadOnlyList<PipelinePolicy> pipeline, int currentIndex)
{
    message.Request.Headers.Add(HeaderName, imageDeployment);
    await ProcessNextAsync(message, pipeline, currentIndex);
}
    }

    public async ValueTask DisposeAsync()
{
    if (_projectClient == null) return;

    foreach (var (name, agent) in _agents)
    {
        try
        {
            await _projectClient.Agents.DeleteAgentVersionAsync(agent.Name, agent.Version);
            _logger.LogInformation("Deleted agent: {Name}", name);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cleanup agent {Name}", name);
        }
    }

    _agents.Clear();
    _agentInfos.Clear();
    GC.SuppressFinalize(this);
}
}
