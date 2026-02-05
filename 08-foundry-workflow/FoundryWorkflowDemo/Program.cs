// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Type is for evaluation purposes only

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
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
var deployment = configuration["Azure:ModelDeploymentName"]
    ?? throw new InvalidOperationException("Azure:ModelDeploymentName is required.");

Console.WriteLine("Lab 08 - Build a workflow in Microsoft Foundry");
Console.WriteLine("This sample demonstrates a simple workflow-like pipeline using multiple agents.");
Console.WriteLine();

Azure.Core.TokenCredential credential = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential();

var projectClient = new AIProjectClient(new Uri(endpoint), credential);

AgentVersion? planner = null;
AgentVersion? writer = null;
AgentVersion? reviewer = null;

try
{
    planner = (await projectClient.Agents.CreateAgentVersionAsync(
        agentName: "WorkflowPlanner",
        options: new AgentVersionCreationOptions(new PromptAgentDefinition(deployment)
        {
            Instructions = "You are a planner. Create a short plan for the requested output."
        }))).Value;

    writer = (await projectClient.Agents.CreateAgentVersionAsync(
        agentName: "WorkflowWriter",
        options: new AgentVersionCreationOptions(new PromptAgentDefinition(deployment)
        {
            Instructions = "You are a writer. Produce a high-quality draft following the provided plan."
        }))).Value;

    reviewer = (await projectClient.Agents.CreateAgentVersionAsync(
        agentName: "WorkflowReviewer",
        options: new AgentVersionCreationOptions(new PromptAgentDefinition(deployment)
        {
            Instructions = "You are a reviewer. Provide concise improvement suggestions and a final revised version."
        }))).Value;

    var plannerClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(planner.Name);
    var writerClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(writer.Name);
    var reviewerClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(reviewer.Name);

    Console.Write("Enter a topic for a short developer blog post (or blank to use default): ");
    var topic = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(topic))
        topic = "How to add MCP tools to a Foundry agent";

    // Step 1: Plan
    var planResponse = await plannerClient.CreateResponseAsync($"Create a short outline/plan for a developer blog post about: {topic}");
    var planText = planResponse.Value.GetOutputText();

    // Step 2: Draft
    var draftResponse = await writerClient.CreateResponseAsync(new CreateResponseOptions
    {
        InputItems =
        {
            ResponseItem.CreateUserMessageItem($"Write the blog post using this plan:\n\n{planText}")
        }
    });
    var draftText = draftResponse.Value.GetOutputText();

    // Step 3: Review + revise
    var reviewResponse = await reviewerClient.CreateResponseAsync(new CreateResponseOptions
    {
        InputItems =
        {
            ResponseItem.CreateUserMessageItem($"Review and improve this draft. Return: (1) brief feedback, (2) revised final version.\n\nDRAFT:\n{draftText}")
        }
    });

    Console.WriteLine("\n=== PLAN ===\n");
    Console.WriteLine(planText);
    Console.WriteLine("\n=== DRAFT ===\n");
    Console.WriteLine(draftText);
    Console.WriteLine("\n=== REVIEW + FINAL ===\n");
    Console.WriteLine(reviewResponse.Value.GetOutputText());
}
finally
{
    async Task DeleteAsync(AgentVersion? agent)
    {
        if (agent is null) return;
        try { await projectClient.Agents.DeleteAgentVersionAsync(agent.Name, agent.Version); } catch { }
    }

    await DeleteAsync(planner);
    await DeleteAsync(writer);
    await DeleteAsync(reviewer);
}
