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

var endpoint =
    configuration["Azure:ProjectEndpoint"]
    ?? throw new InvalidOperationException("Azure:ProjectEndpoint is required.");

var deployment =
    configuration["Azure:ModelDeploymentName"]
    ?? throw new InvalidOperationException("Azure:ModelDeploymentName is required.");

var remoteMsLearnAgentName = configuration["Azure:MsLearnAgentName"] ?? "MSLearn";

Console.WriteLine("Lab 03b - Develop a multi-agent solution with Microsoft Foundry");
Console.WriteLine("This app acts as a front-end agent and calls a remote 'MSLearn' agent for research.");
Console.WriteLine($"Remote agent name: {remoteMsLearnAgentName}");
Console.WriteLine();

Azure.Core.TokenCredential credential = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
    ? new DefaultAzureCredential()
    : new ManagedIdentityCredential();

var projectClient = new AIProjectClient(new Uri(endpoint), credential);

// Create a simple front-end agent (no special tools here; it just synthesizes answers)
var frontEndDefinition = new PromptAgentDefinition(model: deployment)
{
    Instructions = """
		You are a front-end assistant.
		You will be provided with research from another agent (MSLearn).
		Use that research to answer clearly and concisely.
		If the research is insufficient, say what is missing.
		"""
};

var frontEndAgent = (await projectClient.Agents.CreateAgentVersionAsync(
    agentName: "FrontEndAgent",
    options: new AgentVersionCreationOptions(frontEndDefinition))).Value;

Console.WriteLine($"Created front-end agent: {frontEndAgent.Name} (version: {frontEndAgent.Version})");
Console.WriteLine();

var frontEndResponses = projectClient.OpenAI.GetProjectResponsesClientForAgent(frontEndAgent.Name);
var msLearnResponses = projectClient.OpenAI.GetProjectResponsesClientForAgent(remoteMsLearnAgentName);

while (true)
{
    Console.Write("Ask a question (or 'quit'): ");
    var question = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(question) || question.Equals("quit", StringComparison.OrdinalIgnoreCase))
        break;

    // 1) Ask the remote MSLearn agent to do focused research
    var msLearnPrompt = $"Find authoritative Microsoft Learn documentation for: {question}. Provide key points and URLs.";
    var msLearnResult = await msLearnResponses.CreateResponseAsync(msLearnPrompt);
    var research = msLearnResult.Value.GetOutputText();

    // 2) Ask the front-end agent to synthesize a final answer
    var synthesisPrompt = """
		Answer the user's question using the provided research.
		Keep the answer concise and actionable.

		USER QUESTION:
		{question}

		MSLearn RESEARCH:
		{research}
		""";

    var finalResult = await frontEndResponses.CreateResponseAsync(new CreateResponseOptions
    {
        InputItems = { ResponseItem.CreateUserMessageItem(synthesisPrompt) }
    });

    Console.WriteLine();
    Console.WriteLine(finalResult.Value.GetOutputText());
    Console.WriteLine();
}

try
{
    await projectClient.Agents.DeleteAgentVersionAsync(frontEndAgent.Name, frontEndAgent.Version);
}
catch
{
    // best-effort cleanup
}
