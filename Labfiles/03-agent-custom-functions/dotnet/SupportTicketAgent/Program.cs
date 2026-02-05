// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Type is for evaluation purposes only

using System.Net.Http.Json;
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

var skillsBaseUrl = configuration["Skills:BaseUrl"] ?? "http://localhost:7071";
var normalizePath = configuration["Skills:NormalizePath"] ?? "/api/normalize-text";

Console.WriteLine("Lab 03 - Use a custom function in an AI agent");
Console.WriteLine("This sample builds a simple support-ticket assistant.");
Console.WriteLine($"Skills function base URL: {skillsBaseUrl}");
Console.WriteLine();

Azure.Core.TokenCredential credential = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential();

var projectClient = new AIProjectClient(new Uri(endpoint), credential);

var agentDefinition = new PromptAgentDefinition(model: deployment)
{
    Instructions = """
		You are a technical support agent.
		Collect a user's issue description and produce a structured support ticket.
		Keep the ticket concise and actionable.
		Output must be valid JSON.
		"""
};

var agent = (await projectClient.Agents.CreateAgentVersionAsync(
    agentName: "SupportTicketAgent",
    options: new AgentVersionCreationOptions(agentDefinition))).Value;

var responsesClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agent.Name);

using var http = new HttpClient { BaseAddress = new Uri(skillsBaseUrl) };

while (true)
{
    Console.Write("User email (or 'quit'): ");
    var email = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(email) || email.Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;

    Console.Write("Describe the problem: ");
    var description = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(description))
        continue;

    // Custom function call (HTTP) - same function can also be used as an Azure AI Search custom skill.
    string normalizedDescription;
    try
    {
        var normalizeResponse = await http.PostAsJsonAsync(normalizePath, new { text = description });
        normalizeResponse.EnsureSuccessStatusCode();

        var normalizePayload = await normalizeResponse.Content.ReadFromJsonAsync<NormalizeResponse>();
        normalizedDescription = normalizePayload?.NormalizedText ?? description;
    }
    catch
    {
        normalizedDescription = description;
    }

    var prompt = """
		Create a support ticket in JSON using this schema:

		{
		  "ticketId": "string",
		  "submittedBy": "string",
		  "summary": "string",
		  "description": "string",
		  "normalizedDescription": "string",
		  "severity": "low|medium|high",
		  "category": "string",
		  "suggestedNextSteps": ["string"]
		}

		Use these inputs:
		- submittedBy: {email}
		- description: {description}
		- normalizedDescription: {normalizedDescription}
		""";

    var result = await responsesClient.CreateResponseAsync(new CreateResponseOptions
    {
        InputItems = { ResponseItem.CreateUserMessageItem(prompt) }
    });

    Console.WriteLine();
    Console.WriteLine(result.Value.GetOutputText());
    Console.WriteLine();
}

try
{
    await projectClient.Agents.DeleteAgentVersionAsync(agent.Name, agent.Version);
}
catch
{
    // best-effort cleanup
}

internal sealed class NormalizeResponse
{
    public string? NormalizedText { get; set; }
}
