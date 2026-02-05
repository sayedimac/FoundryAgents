// Copyright (c) Microsoft. All rights reserved.

#pragma warning disable OPENAI001 // Type is for evaluation purposes only

using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using OpenAI.Responses;

namespace GitHubAgent;

internal static class WorkflowCore
{
    // GitHub MCP Server URL - can be configured via environment variable
    private const string DefaultGitHubMcpServerUrl = "https://api.githubcopilot.com/mcp";

    public static async ValueTask RunAsync(bool containerMode = false)
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.Development.json", optional: true)
            .AddEnvironmentVariables()
            .Build();

        var endpoint =
            configuration["Azure:ProjectEndpoint"]
            ?? throw new InvalidOperationException(
                "Azure:ProjectEndpoint is required. Set it in appsettings.Development.json for local development or as Azure__ProjectEndpoint environment variable for production");
        var deployment =
            configuration["Azure:ModelDeploymentName"]
            ?? throw new InvalidOperationException(
            "Azure:ModelDeploymentName is required. Set it in appsettings.Development.json for local development or as Azure__ModelDeploymentName environment variable for containers");

        // Optional: GitHub MCP server URL (can be customized)
        var gitHubMcpUrl = configuration["Azure:GitHubMcpServerUrl"] ?? DefaultGitHubMcpServerUrl;

        Console.WriteLine($"Using Azure AI endpoint: {endpoint}");
        Console.WriteLine($"Using model deployment: {deployment}");
        Console.WriteLine($"GitHub MCP Server: {gitHubMcpUrl}");

        // Create credential - use ManagedIdentityCredential if MSI_ENDPOINT exists, otherwise DefaultAzureCredential
        Azure.Core.TokenCredential credential = string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MSI_ENDPOINT"))
            ? new DefaultAzureCredential()
            : new ManagedIdentityCredential();

        // Create AIProjectClient (new Foundry SDK)
        var projectClient = new AIProjectClient(new Uri(endpoint), credential);

        AgentVersion? writerAgent = null;
        AgentVersion? reviewerAgent = null;
        AgentVersion? githubAgent = null;

        try
        {
            // Create Writer agent using new Foundry patterns
            writerAgent = await CreateAgentAsync(
                projectClient,
                deployment,
                "Writer",
                "You are an excellent content writer. You create new content and edit contents based on the feedback."
            );

            // Create Reviewer agent
            reviewerAgent = await CreateAgentAsync(
                projectClient,
                deployment,
                "Reviewer",
                "You are an excellent content reviewer. Provide actionable feedback to the writer about the provided content. Provide the feedback in the most concise manner possible."
            );

            // Create GitHub agent with MCP capabilities
            githubAgent = await CreateGitHubAgentAsync(
                projectClient,
                deployment,
                gitHubMcpUrl
            );

            Console.WriteLine();

            if (containerMode)
            {
                // For container mode, run a simple loop
                await RunContainerModeAsync(projectClient, writerAgent, reviewerAgent, githubAgent);
            }
            else
            {
                // For interactive mode, run the multi-agent workflow
                await RunInteractiveAsync(projectClient, writerAgent, reviewerAgent, githubAgent);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error running workflow: {ex.Message}");
            throw;
        }
        finally
        {
            // Clean up all agent versions
            await CleanupAsync(projectClient, writerAgent);
            await CleanupAsync(projectClient, reviewerAgent);
            await CleanupAsync(projectClient, githubAgent);
        }
    }

    private static async Task RunInteractiveAsync(
        AIProjectClient projectClient,
        AgentVersion writerAgent,
        AgentVersion reviewerAgent,
        AgentVersion githubAgent)
    {
        Console.WriteLine("\n=== Interactive Mode ===");
        Console.WriteLine("Available agents: Writer, Reviewer, GitHub");
        Console.WriteLine("Commands: 'writer', 'reviewer', 'github', 'workflow', 'quit'\n");

        while (true)
        {
            Console.Write("Select agent or command: ");
            var command = Console.ReadLine()?.Trim().ToLowerInvariant();

            if (string.IsNullOrEmpty(command) || command == "quit")
                break;

            switch (command)
            {
                case "writer":
                    await ChatWithAgentAsync(projectClient, writerAgent, "Writer");
                    break;
                case "reviewer":
                    await ChatWithAgentAsync(projectClient, reviewerAgent, "Reviewer");
                    break;
                case "github":
                    await ChatWithGitHubAgentAsync(projectClient, githubAgent);
                    break;
                case "workflow":
                    await RunWriterReviewerWorkflowAsync(projectClient, writerAgent, reviewerAgent);
                    break;
                default:
                    Console.WriteLine("Unknown command. Use: writer, reviewer, github, workflow, or quit");
                    break;
            }
        }
    }

    private static async Task ChatWithAgentAsync(
        AIProjectClient projectClient,
        AgentVersion agent,
        string agentName)
    {
        Console.Write($"\n[{agentName}] Enter your prompt: ");
        var prompt = Console.ReadLine();
        if (string.IsNullOrEmpty(prompt)) return;

        Console.WriteLine($"\n=== {agentName} Agent ===");
        var responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(agent.Name);

        await foreach (var update in responseClient.CreateResponseStreamingAsync(prompt))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
            {
                Console.Write(textDelta.Delta);
            }
        }
        Console.WriteLine("\n");
    }

    private static async Task ChatWithGitHubAgentAsync(
        AIProjectClient projectClient,
        AgentVersion githubAgent)
    {
        Console.Write("\n[GitHub] Enter your prompt (e.g., 'List issues in repo X', 'Search for code'): ");
        var prompt = Console.ReadLine();
        if (string.IsNullOrEmpty(prompt)) return;

        Console.WriteLine("\n=== GitHub Agent (with MCP) ===");
        var responseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(githubAgent.Name);

        // Create response options for MCP tool usage
        var responseOptions = new CreateResponseOptions
        {
            InputItems = { ResponseItem.CreateUserMessageItem(prompt) }
        };

        ResponseResult? latestResponse = null;
        CreateResponseOptions? nextResponseOptions = responseOptions;

        // Handle MCP tool approval loop
        while (nextResponseOptions is not null)
        {
            latestResponse = await responseClient.CreateResponseAsync(nextResponseOptions);
            nextResponseOptions = null;

            foreach (var responseItem in latestResponse.OutputItems)
            {
                if (responseItem is McpToolCallApprovalRequestItem mcpToolCall)
                {
                    // Auto-approve GitHub MCP tool calls
                    Console.WriteLine($"  [MCP] Approving tool call: {mcpToolCall.ServerLabel}");
                    nextResponseOptions = new CreateResponseOptions
                    {
                        PreviousResponseId = latestResponse.Id
                    };
                    nextResponseOptions.InputItems.Add(
                        ResponseItem.CreateMcpApprovalResponseItem(
                            approvalRequestId: mcpToolCall.Id,
                            approved: true));
                }
            }
        }

        if (latestResponse is not null)
        {
            Console.WriteLine(latestResponse.GetOutputText());
        }
        Console.WriteLine();
    }

    private static async Task RunWriterReviewerWorkflowAsync(
        AIProjectClient projectClient,
        AgentVersion writerAgent,
        AgentVersion reviewerAgent)
    {
        var prompt = "Create a slogan for a new electric SUV that is affordable and fun to drive.";
        Console.WriteLine($"\nUser: {prompt}\n");

        // Step 1: Writer creates content
        Console.WriteLine("=== Writer Agent ===");
        var writerResponseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(writerAgent.Name);

        string writerOutput = "";
        await foreach (var update in writerResponseClient.CreateResponseStreamingAsync(prompt))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
            {
                Console.Write(textDelta.Delta);
                writerOutput += textDelta.Delta;
            }
        }
        Console.WriteLine("\n");

        // Step 2: Reviewer provides feedback
        Console.WriteLine("=== Reviewer Agent ===");
        var reviewerResponseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(reviewerAgent.Name);

        var reviewPrompt = $"Please review this content and provide feedback:\n\n{writerOutput}";
        await foreach (var update in reviewerResponseClient.CreateResponseStreamingAsync(reviewPrompt))
        {
            if (update is StreamingResponseOutputTextDeltaUpdate textDelta)
            {
                Console.Write(textDelta.Delta);
            }
        }
        Console.WriteLine("\n");
    }

    private static async Task RunContainerModeAsync(
        AIProjectClient projectClient,
        AgentVersion writerAgent,
        AgentVersion reviewerAgent,
        AgentVersion githubAgent)
    {
        Console.WriteLine("Running in container mode. Enter prompts (type 'quit' to exit):");
        Console.WriteLine("Prefix with 'github:' for GitHub agent, otherwise uses writer/reviewer workflow.");

        while (true)
        {
            Console.Write("\nYou: ");
            var input = Console.ReadLine();

            if (string.IsNullOrEmpty(input) || input.Equals("quit", StringComparison.OrdinalIgnoreCase))
                break;

            if (input.StartsWith("github:", StringComparison.OrdinalIgnoreCase))
            {
                var githubPrompt = input[7..].Trim();
                await ChatWithGitHubAgentAsync(projectClient, githubAgent);
            }
            else
            {
                // Writer creates content
                Console.WriteLine("\n=== Writer ===");
                var writerResponseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(writerAgent.Name);
                var writerResponse = await writerResponseClient.CreateResponseAsync(input);
                var writerOutput = writerResponse.Value.GetOutputText();
                Console.WriteLine(writerOutput);

                // Reviewer provides feedback
                Console.WriteLine("\n=== Reviewer ===");
                var reviewerResponseClient = projectClient.OpenAI.GetProjectResponsesClientForAgent(reviewerAgent.Name);
                var reviewerResponse = await reviewerResponseClient.CreateResponseAsync(
                    $"Please review this content and provide feedback:\n\n{writerOutput}");
                Console.WriteLine(reviewerResponse.Value.GetOutputText());
            }
        }
    }

    private static async Task<AgentVersion> CreateAgentAsync(
        AIProjectClient projectClient,
        string model,
        string name,
        string instructions)
    {
        // Define the agent using new Foundry PromptAgentDefinition
        var agentDefinition = new PromptAgentDefinition(model: model)
        {
            Instructions = instructions
        };

        // Create agent version
        var agentVersion = await projectClient.Agents.CreateAgentVersionAsync(
            agentName: name,
            options: new AgentVersionCreationOptions(agentDefinition));

        Console.WriteLine($"Created agent: {agentVersion.Value.Name} (version: {agentVersion.Value.Version})");
        return agentVersion.Value;
    }

    private static async Task<AgentVersion> CreateGitHubAgentAsync(
        AIProjectClient projectClient,
        string model,
        string gitHubMcpUrl)
    {
        // Create MCP tool for GitHub
        var gitHubMcpTool = ResponseTool.CreateMcpTool(
            serverLabel: "github",
            serverUri: new Uri(gitHubMcpUrl),
            toolCallApprovalPolicy: new McpToolCallApprovalPolicy(
                GlobalMcpToolCallApprovalPolicy.AlwaysRequireApproval)
        );

        // Define the GitHub agent with MCP capabilities
        var agentDefinition = new PromptAgentDefinition(model: model)
        {
            Instructions = """
                You are a helpful GitHub assistant with access to GitHub via MCP (Model Context Protocol).
                You can help users with:
                - Searching for repositories, code, issues, and pull requests
                - Getting information about repositories, commits, and branches
                - Listing issues and pull requests
                - Searching for users and organizations
                
                Use the GitHub MCP tools available to you to answer questions and perform tasks.
                Always be helpful and provide clear, concise responses.
                """,
            Tools = { gitHubMcpTool }
        };

        // Create agent version
        var agentVersion = await projectClient.Agents.CreateAgentVersionAsync(
            agentName: "GitHubAgent",
            options: new AgentVersionCreationOptions(agentDefinition));

        Console.WriteLine($"Created GitHub agent: {agentVersion.Value.Name} (version: {agentVersion.Value.Version}) with MCP");
        return agentVersion.Value;
    }

    private static async Task CleanupAsync(AIProjectClient projectClient, AgentVersion? agentVersion)
    {
        if (agentVersion is null)
            return;

        try
        {
            await projectClient.Agents.DeleteAgentVersionAsync(
                agentName: agentVersion.Name,
                agentVersion: agentVersion.Version);
            Console.WriteLine($"Deleted agent: {agentVersion.Name}");
        }
        catch (Exception e)
        {
            Console.WriteLine($"Cleanup failed for agent {agentVersion.Name}: {e.Message}");
        }
    }
}
