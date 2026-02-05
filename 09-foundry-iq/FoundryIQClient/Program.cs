// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Type is for evaluation purposes only

using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Responses;

var configuration = new ConfigurationBuilder()
    .SetBasePath(Directory.GetCurrentDirectory())
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

var endpoint = configuration["Azure:ProjectEndpoint"]
    ?? throw new InvalidOperationException("Azure:ProjectEndpoint is required.");

// This lab assumes you've created an agent in the Foundry portal and enabled Foundry IQ knowledge.
var agentName = configuration["Azure:FoundryIqAgentName"] ?? "FoundryIQAgent";

Console.WriteLine("Lab 09 - Integrate an AI agent with Foundry IQ");
Console.WriteLine($"Foundry Project: {endpoint}");
Console.WriteLine($"Agent (portal-created): {agentName}");
Console.WriteLine();

Azure.Core.TokenCredential credential = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential();

var projectClient = new AIProjectClient(new Uri(endpoint), credential);
var responsesClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agentName);

while (true)
{
    Console.Write("Ask a question (or 'quit'): ");
    var prompt = Console.ReadLine();

    if (string.IsNullOrWhiteSpace(prompt) || prompt.Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;

    var responseOptions = new CreateResponseOptions
    {
        InputItems = { ResponseItem.CreateUserMessageItem(prompt) }
    };

    ResponseResult? latest = null;
    CreateResponseOptions? next = responseOptions;

    // Approval loop: Foundry IQ access is mediated via MCP-style approval requests.
    while (next is not null)
    {
        latest = await responsesClient.CreateResponseAsync(next);
        next = null;

        foreach (var item in latest.OutputItems)
        {
            if (item is McpToolCallApprovalRequestItem approval)
            {
                Console.WriteLine($"[MCP] Agent requested approval to use tool server: {approval.ServerLabel}");
                Console.Write("Approve? (y/n): ");
                var decision = Console.ReadLine()?.Trim().ToLowerInvariant();
                var approved = decision is "y" or "yes";

                next = new CreateResponseOptions { PreviousResponseId = latest.Id };
                next.InputItems.Add(ResponseItem.CreateMcpApprovalResponseItem(
                    approvalRequestId: approval.Id,
                    approved: approved));
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
