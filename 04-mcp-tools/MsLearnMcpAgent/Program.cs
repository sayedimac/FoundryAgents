// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Type is for evaluation purposes only

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Responses;

const string defaultMcpServerUrl = "https://learn.microsoft.com/api/mcp";

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var endpoint =
    configuration["Azure:ProjectEndpoint"]
    ?? throw new InvalidOperationException(
        "Azure:ProjectEndpoint is required. Set it in appsettings.Development.json or AZURE__PROJECTENDPOINT.");

var deployment =
    configuration["Azure:ModelDeploymentName"]
    ?? throw new InvalidOperationException(
        "Azure:ModelDeploymentName is required. Set it in appsettings.Development.json or AZURE__MODELDEPLOYMENTNAME.");

var mcpServerUrl = configuration["Azure:MsLearnMcpServerUrl"] ?? defaultMcpServerUrl;

Console.WriteLine("Lab 03c - Connect AI Agents to a remote MCP server");
Console.WriteLine($"Foundry Project: {endpoint}");
Console.WriteLine($"Model deployment: {deployment}");
Console.WriteLine($"MS Learn MCP server: {mcpServerUrl}");
Console.WriteLine();

Azure.Core.TokenCredential credential = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential();

var projectClient = new AIProjectClient(new Uri(endpoint), credential);

var mcpTool = ResponseTool.CreateMcpTool(
    serverLabel: "mslearn",
    serverUri: new Uri(mcpServerUrl),
    toolCallApprovalPolicy: new McpToolCallApprovalPolicy(GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval));

var agentDefinition = new PromptAgentDefinition(model: deployment)
{
    Instructions = """
		You are a helpful Microsoft Learn assistant.
		Use the MCP tools provided by the Microsoft Learn MCP server to answer questions with up-to-date official documentation.
		When you cite information, include the Microsoft Learn URL(s) you used.
		""",
    Tools = { mcpTool }
};

var agent = (await projectClient.Agents.CreateAgentVersionAsync(
    agentName: "MSLearnMcpAgent",
    options: new AgentVersionCreationOptions(agentDefinition))).Value;

Console.WriteLine($"Created agent: {agent.Name} (version: {agent.Version})");
Console.WriteLine();

var responsesClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agent.Name);

while (true)
{
    Console.WriteLine("Choose an MCP scenario:");
    Console.WriteLine("  1) Search docs (microsoft_docs_search)");
    Console.WriteLine("  2) Fetch a doc page (microsoft_docs_fetch)");
    Console.WriteLine("  3) Search code samples (microsoft_code_sample_search)");
    Console.WriteLine("  q) Quit");
    Console.Write("> ");
    var scenario = Console.ReadLine()?.Trim().ToLowerInvariant();

    if (scenario is null || scenario == "q" || scenario == "quit")
        break;

    Console.Write("Your question / query: ");
    var userQuery = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(userQuery))
        continue;

    var prompt = scenario switch
    {
        "1" => $"Use microsoft_docs_search to answer: {userQuery}",
        "2" => $"Use microsoft_docs_fetch to retrieve the most relevant page for: {userQuery}. Then summarize it.",
        "3" => $"Use microsoft_code_sample_search to find code samples for: {userQuery}",
        _ => userQuery,
    };

    var responseOptions = new CreateResponseOptions
    {
        InputItems = { ResponseItem.CreateUserMessageItem(prompt) }
    };

    ResponseResult? latest = null;
    CreateResponseOptions? next = responseOptions;

    while (next is not null)
    {
        latest = await responsesClient.CreateResponseAsync(next);
        next = null;

        foreach (var item in latest.OutputItems)
        {
            if (item is McpToolCallApprovalRequestItem approval)
            {
                Console.WriteLine($"[MCP] Approving tool call: {approval.ServerLabel}");
                next = new CreateResponseOptions { PreviousResponseId = latest.Id };
                next.InputItems.Add(ResponseItem.CreateMcpApprovalResponseItem(
                    approvalRequestId: approval.Id,
                    approved: true));
            }
        }
    }

    if (latest is not null)
    {
        Console.WriteLine();
        Console.WriteLine(latest.GetOutputText());
        Console.WriteLine();
    }
}

try
{
    await projectClient.Agents.DeleteAgentVersionAsync(agent.Name, agent.Version);
}
catch
{
    // best-effort cleanup
}
