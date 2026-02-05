// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Type is for evaluation purposes only

using System.Collections.Concurrent;
using System.Net.Http.Json;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Responses;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient();
builder.Services.AddSingleton<FoundryAgentsService>();

var app = builder.Build();

// Best-effort cleanup for agent versions created by this web app.
var agentsService = app.Services.GetRequiredService<FoundryAgentsService>();
app.Lifetime.ApplicationStopping.Register(() =>
{
    try { agentsService.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
});

app.MapGet("/", () => Results.Content(Ui.Html, "text/html"));

app.MapPost("/api/chat", async (ChatRequest request, FoundryAgentsService svc) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest(new { error = "message is required" });

    var client = await svc.GetResponsesClientAsync(
        agentName: "WebChatAgent",
        instructions: "You are a helpful assistant. Be concise and actionable.");

    var result = await client.CreateResponseAsync(new CreateResponseOptions
    {
        InputItems = { ResponseItem.CreateUserMessageItem(request.Message) }
    });

    return Results.Ok(new { output = result.Value.GetOutputText() });
});

app.MapPost("/api/support-ticket", async (SupportTicketRequest request, FoundryAgentsService svc, IHttpClientFactory httpFactory, IConfiguration config) =>
{
    if (string.IsNullOrWhiteSpace(request.SubmittedBy) || string.IsNullOrWhiteSpace(request.Description))
        return Results.BadRequest(new { error = "submittedBy and description are required" });

    var skillsBaseUrl = config["Skills:BaseUrl"];
    var normalizePath = config["Skills:NormalizePath"] ?? "/api/normalize-text";

    string normalizedDescription = request.Description;

    if (!string.IsNullOrWhiteSpace(skillsBaseUrl))
    {
        try
        {
            using var http = httpFactory.CreateClient();
            http.BaseAddress = new Uri(skillsBaseUrl);

            var normalizeResponse = await http.PostAsJsonAsync(normalizePath, new { text = request.Description });
            normalizeResponse.EnsureSuccessStatusCode();

            var normalizePayload = await normalizeResponse.Content.ReadFromJsonAsync<NormalizeResponse>();
            if (!string.IsNullOrWhiteSpace(normalizePayload?.NormalizedText))
                normalizedDescription = normalizePayload.NormalizedText;
        }
        catch
        {
            // If the local function app isn't running, just fall back.
        }
    }

    var client = await svc.GetResponsesClientAsync(
        agentName: "WebSupportTicketAgent",
        instructions: "You are a technical support agent. Output must be valid JSON and match the requested schema.");

    var prompt = $$"""
Create a support ticket in JSON using this schema:

{
  \"ticketId\": \"string\",
  \"submittedBy\": \"string\",
  \"summary\": \"string\",
  \"description\": \"string\",
  \"normalizedDescription\": \"string\",
  \"severity\": \"low\"|\"medium\"|\"high\",
  \"category\": \"string\",
  \"suggestedNextSteps\": [\"string\"]
}

Use these inputs:
- submittedBy: {{request.SubmittedBy}}
- description: {{request.Description}}
- normalizedDescription: {{normalizedDescription}}
""";

    var result = await client.CreateResponseAsync(prompt);

    return Results.Ok(new { output = result.Value.GetOutputText(), normalizedDescription });
});

app.MapPost("/api/workflow/blogpost", async (WorkflowRequest request, FoundryAgentsService svc) =>
{
    var topic = string.IsNullOrWhiteSpace(request.Topic)
        ? "How to add MCP tools to a Foundry agent"
        : request.Topic;

    var planner = await svc.GetResponsesClientAsync(
        agentName: "WebWorkflowPlanner",
        instructions: "You are a planner. Create a short outline/plan for the requested output.");

    var writer = await svc.GetResponsesClientAsync(
        agentName: "WebWorkflowWriter",
        instructions: "You are a writer. Produce a high-quality draft following the provided plan.");

    var reviewer = await svc.GetResponsesClientAsync(
        agentName: "WebWorkflowReviewer",
        instructions: "You are a reviewer. Provide brief feedback then a revised final version.");

    var planResponse = await planner.CreateResponseAsync($"Create a short outline/plan for a developer blog post about: {topic}");
    var plan = planResponse.Value.GetOutputText();

    var draftResponse = await writer.CreateResponseAsync(new CreateResponseOptions
    {
        InputItems = { ResponseItem.CreateUserMessageItem($"Write the blog post using this plan:\n\n{plan}") }
    });
    var draft = draftResponse.Value.GetOutputText();

    var reviewResponse = await reviewer.CreateResponseAsync(new CreateResponseOptions
    {
        InputItems =
        {
            ResponseItem.CreateUserMessageItem($"Review and improve this draft. Return: (1) brief feedback, (2) revised final version.\n\nDRAFT:\n{draft}")
        }
    });

    return Results.Ok(new { topic, plan, draft, reviewAndFinal = reviewResponse.Value.GetOutputText() });
});

app.Run();

internal sealed record ChatRequest(string Message);
internal sealed record SupportTicketRequest(string SubmittedBy, string Description);
internal sealed record WorkflowRequest(string? Topic);

internal sealed class NormalizeResponse
{
    public string? NormalizedText { get; set; }
}

internal sealed class FoundryAgentsService : IAsyncDisposable
{
    private readonly IConfiguration _config;

    private readonly SemaphoreSlim _initLock = new(1, 1);
    private AIProjectClient? _projectClient;
    private string? _deployment;

    private readonly ConcurrentDictionary<string, AgentVersion> _agents = new(StringComparer.OrdinalIgnoreCase);

    public FoundryAgentsService(IConfiguration config)
    {
        _config = config;
    }

    public async Task<ProjectResponsesClient> GetResponsesClientAsync(string agentName, string instructions)
    {
        await EnsureInitializedAsync();

        if (!_agents.TryGetValue(agentName, out AgentVersion? agent))
        {
            var definition = new PromptAgentDefinition(_deployment!) { Instructions = instructions };

            agent = (await _projectClient!.Agents.CreateAgentVersionAsync(
                agentName: agentName,
                options: new AgentVersionCreationOptions(definition))).Value;

            _agents[agentName] = agent;
        }

        return _projectClient!.OpenAI.GetProjectResponsesClientForAgent(agent.Name);
    }

    private async Task EnsureInitializedAsync()
    {
        if (_projectClient is not null)
            return;

        await _initLock.WaitAsync();
        try
        {
            if (_projectClient is not null)
                return;

            var endpoint = _config["Azure:ProjectEndpoint"];
            _deployment = _config["Azure:ModelDeploymentName"];

            if (string.IsNullOrWhiteSpace(endpoint))
                throw new InvalidOperationException("Azure:ProjectEndpoint is required.");
            if (string.IsNullOrWhiteSpace(_deployment))
                throw new InvalidOperationException("Azure:ModelDeploymentName is required.");

            // Mirror the lab behavior: DefaultAzureCredential locally; ManagedIdentity when hosted.
            TokenCredential credential = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
                ? new DefaultAzureCredential()
                : new ManagedIdentityCredential();

            _projectClient = new AIProjectClient(new Uri(endpoint), credential);
        }
        finally
        {
            _initLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_projectClient is null)
            return;

        foreach (AgentVersion agent in _agents.Values)
        {
            try { await _projectClient.Agents.DeleteAgentVersionAsync(agent.Name, agent.Version); } catch { }
        }

        _agents.Clear();
    }
}

internal static class Ui
{
    public const string Html = """
<!doctype html>
<html lang=\"en\">
<head>
  <meta charset=\"utf-8\" />
  <meta name=\"viewport\" content=\"width=device-width, initial-scale=1\" />
  <title>Agent Web App (ASP.NET Core + Azure AI Foundry)</title>
  <style>
    body { font-family: system-ui, Segoe UI, Arial; margin: 24px; max-width: 980px; }
    textarea, input { width: 100%; font-family: inherit; }
    textarea { min-height: 110px; }
    .row { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
    pre { white-space: pre-wrap; background: #111; color: #eee; padding: 12px; border-radius: 6px; }
    button { padding: 8px 12px; }
    .card { border: 1px solid #ddd; border-radius: 8px; padding: 16px; margin: 14px 0; }
    .muted { color: #555; }
  </style>
</head>
<body>
  <h1>Agent Web App (ASP.NET Core + Azure AI Foundry)</h1>
  <p class=\"muted\">
    Configure <code>Azure:ProjectEndpoint</code> and <code>Azure:ModelDeploymentName</code> in <code>appsettings.Development.json</code> (or env vars), then run:
    <code>dotnet run --project .\\AgentWebApp.csproj</code>
  </p>

  <div class=\"card\">
    <h2>1) Chat</h2>
    <textarea id=\"chatMessage\" placeholder=\"Ask something...\"></textarea>
    <button onclick=\"runChat()\">Send</button>
    <pre id=\"chatOut\"></pre>
  </div>

  <div class=\"card\">
    <h2>2) Support ticket (custom function optional)</h2>
    <div class=\"row\">
      <div>
        <input id=\"ticketEmail\" placeholder=\"user@contoso.com\" />
      </div>
      <div>
        <span class=\"muted\">Optional: set <code>Skills:BaseUrl</code> to call the Lab 03 function app</span>
      </div>
    </div>
    <textarea id=\"ticketDesc\" placeholder=\"Describe the issue...\"></textarea>
    <button onclick=\"runTicket()\">Create ticket</button>
    <pre id=\"ticketOut\"></pre>
  </div>

  <div class=\"card\">
    <h2>3) Workflow (plan → draft → review)</h2>
    <input id=\"wfTopic\" placeholder=\"Topic (optional)\" />
    <button onclick=\"runWorkflow()\">Run workflow</button>
    <pre id=\"wfOut\"></pre>
  </div>

<script>
async function postJson(url, body) {
  const res = await fetch(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body)
  });
  const text = await res.text();
  try { return { ok: res.ok, json: JSON.parse(text) }; }
  catch { return { ok: res.ok, json: { raw: text } }; }
}

async function runChat() {
  const out = document.getElementById('chatOut');
  out.textContent = 'Working...';
  const msg = document.getElementById('chatMessage').value;
  const result = await postJson('/api/chat', { message: msg });
  out.textContent = JSON.stringify(result.json, null, 2);
}

async function runTicket() {
  const out = document.getElementById('ticketOut');
  out.textContent = 'Working...';
  const submittedBy = document.getElementById('ticketEmail').value;
  const description = document.getElementById('ticketDesc').value;
  const result = await postJson('/api/support-ticket', { submittedBy, description });
  out.textContent = JSON.stringify(result.json, null, 2);
}

async function runWorkflow() {
  const out = document.getElementById('wfOut');
  out.textContent = 'Working...';
  const topic = document.getElementById('wfTopic').value;
  const result = await postJson('/api/workflow/blogpost', { topic });
  out.textContent = JSON.stringify(result.json, null, 2);
}
</script>

</body>
</html>
""";
}
