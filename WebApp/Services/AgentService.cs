#pragma warning disable OPENAI001

using System.Runtime.CompilerServices;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
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
    private bool _initialized;
    private readonly SemaphoreSlim _initLock = new(1, 1);

    public AgentService(IConfiguration configuration, ILogger<AgentService> logger)
    {
        _configuration = configuration;
        _logger = logger;

        var endpoint = configuration["Azure:ProjectEndpoint"];
        if (string.IsNullOrEmpty(endpoint))
        {
            _logger.LogWarning("Azure:ProjectEndpoint not configured. Agent features will be disabled.");
            return;
        }

        Azure.Core.TokenCredential credential = string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential();

        _projectClient = new AIProjectClient(new Uri(endpoint), credential);
    }

    public async Task InitializeAsync()
    {
        if (_initialized || _projectClient == null) return;

        await _initLock.WaitAsync();
        try
        {
            if (_initialized) return;

            var deployment = _configuration["Azure:ModelDeploymentName"]
                ?? throw new InvalidOperationException("Azure:ModelDeploymentName is required");

            // Create Writer Agent
            _agents["Writer"] = await CreateAgentAsync(deployment, "ChatWriter", """
                You are an excellent content writer. You create new content and
                edit contents based on feedback. Format your responses in Markdown.
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
                You are an excellent content reviewer. Provide actionable feedback
                in a constructive manner. Use Markdown formatting for structure.
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
            _agents["CodeAssistant"] = await CreateAgentAsync(deployment, "ChatCodeAssistant", """
                You are a helpful code assistant. You help with programming questions,
                code review, debugging, and explaining code. Always use proper Markdown
                code blocks with language identifiers for syntax highlighting.
                Be concise but thorough in your explanations.
                """);
            _agentInfos["CodeAssistant"] = new ChatAgentInfo
            {
                Name = "CodeAssistant",
                Description = "Helps with coding questions",
                Avatar = "C",
                Capabilities = ["Code review", "Debugging", "Explanations", "Best practices"],
                HasTools = false
            };

            // Create GitHub Agent with MCP (if configured)
            var mcpUrl = _configuration["Azure:GitHubMcpServerUrl"];
            if (!string.IsNullOrEmpty(mcpUrl))
            {
                try
                {
                    _agents["GitHub"] = await CreateGitHubAgentAsync(deployment, mcpUrl);
                    _agentInfos["GitHub"] = new ChatAgentInfo
                    {
                        Name = "GitHub",
                        Description = "GitHub assistant with MCP tools",
                        Avatar = "G",
                        Capabilities = ["Repository search", "Issue tracking", "Code search", "PR management"],
                        HasTools = true
                    };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to create GitHub agent with MCP. Skipping.");
                }
            }

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
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await InitializeAsync();

        if (_projectClient == null)
        {
            yield return new TextDeltaUpdate("Agent service is not configured. Please set Azure:ProjectEndpoint in configuration.");
            yield break;
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

        // Handle MCP tool approval loop for GitHub agent (uses non-streaming API)
        if (agentName == "GitHub")
        {
            // Create non-streaming options for MCP handling
            var mcpOptions = new CreateResponseOptions();
            foreach (var msg in history.TakeLast(20))
            {
                mcpOptions.InputItems.Add(msg.Role == "user"
                    ? ResponseItem.CreateUserMessageItem(msg.Content)
                    : ResponseItem.CreateAssistantMessageItem(msg.Content));
            }
            mcpOptions.InputItems.Add(ResponseItem.CreateUserMessageItem(prompt));

            await foreach (var update in HandleMcpStreamingAsync(responseClient, mcpOptions, cancellationToken))
            {
                yield return update;
            }
        }
        else
        {
            await foreach (var update in responseClient.CreateResponseStreamingAsync(options, cancellationToken))
            {
                if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
                {
                    yield return new TextDeltaUpdate(textDelta.Delta);
                }
            }
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

            foreach (var item in latestResponse.OutputItems)
            {
                if (item is McpToolCallApprovalRequestItem mcpApproval)
                {
                    yield return new ToolCallUpdate(mcpApproval.ServerLabel, "Requesting approval...");

                    // Auto-approve for demo purposes
                    nextOptions = new CreateResponseOptions
                    {
                        PreviousResponseId = latestResponse.Id
                    };
                    nextOptions.InputItems.Add(ResponseItem.CreateMcpApprovalResponseItem(
                        approvalRequestId: mcpApproval.Id,
                        approved: true));

                    yield return new ToolResultUpdate(mcpApproval.ServerLabel, "Approved");
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
            return Task.FromResult<IEnumerable<ChatAgentInfo>>(new[]
            {
                new ChatAgentInfo { Name = "Writer", Description = "Creates and edits content", Avatar = "W" },
                new ChatAgentInfo { Name = "Reviewer", Description = "Reviews and provides feedback", Avatar = "R" },
                new ChatAgentInfo { Name = "CodeAssistant", Description = "Helps with coding questions", Avatar = "C" },
                new ChatAgentInfo { Name = "GitHub", Description = "GitHub assistant with tools", Avatar = "G", HasTools = true }
            });
        }

        return Task.FromResult<IEnumerable<ChatAgentInfo>>(_agentInfos.Values);
    }

    private async Task<AgentVersion> CreateAgentAsync(string model, string name, string instructions)
    {
        var definition = new PromptAgentDefinition(model: model)
        {
            Instructions = instructions
        };

        var result = await _projectClient!.Agents.CreateAgentVersionAsync(
            agentName: name,
            options: new AgentVersionCreationOptions(definition));

        _logger.LogInformation("Created agent: {Name} v{Version}", result.Value.Name, result.Value.Version);
        return result.Value;
    }

    private async Task<AgentVersion> CreateGitHubAgentAsync(string model, string mcpUrl)
    {
        var mcpTool = ResponseTool.CreateMcpTool(
            serverLabel: "github",
            serverUri: new Uri(mcpUrl),
            toolCallApprovalPolicy: new McpToolCallApprovalPolicy(
                GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval));

        var definition = new PromptAgentDefinition(model: model)
        {
            Instructions = """
                You are a helpful GitHub assistant with access to GitHub via MCP.
                You can search repositories, issues, pull requests, and code.
                Format responses in Markdown. Include relevant links when helpful.
                Be concise but informative.
                """,
            Tools = { mcpTool }
        };

        var result = await _projectClient!.Agents.CreateAgentVersionAsync(
            agentName: "ChatGitHubAgent",
            options: new AgentVersionCreationOptions(definition));

        _logger.LogInformation("Created GitHub agent with MCP: {Name} v{Version}", result.Value.Name, result.Value.Version);
        return result.Value;
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
