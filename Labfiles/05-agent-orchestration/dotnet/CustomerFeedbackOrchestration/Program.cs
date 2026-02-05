using System.Text;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("Microsoft Learn Agent - Lab 05 (Agent orchestration / Sequential workflow)");
Console.WriteLine("-----------------------------------------------------------------");

IConfiguration config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

string endpoint = GetRequiredSetting(config, "AZURE_OPENAI_ENDPOINT", "Azure:OpenAIEndpoint");
string deploymentName = GetRequiredSetting(config, "AZURE_OPENAI_DEPLOYMENT_NAME", "Azure:OpenAIDeploymentName");
string? apiKey = GetOptionalSetting(config, "AZURE_OPENAI_KEY", "Azure:OpenAIKey");

AzureOpenAIClient openAiClient = !string.IsNullOrWhiteSpace(apiKey)
    ? new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
    : new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential());

IChatClient chatClient = openAiClient
    .GetChatClient(deploymentName)
    .AsIChatClient();

ChatClientAgent summarizer = new(
    chatClient,
    "You summarize customer feedback for a product team. Output 3-5 bullet points. Be faithful to the input.")
{
    Id = "Summarizer"
};

ChatClientAgent sentiment = new(
    chatClient,
    "You analyze customer feedback sentiment. Output JSON only: { \"sentiment\": \"positive\"|\"neutral\"|\"negative\", \"confidence\": number (0-1), \"rationale\": string }.")
{
    Id = "Sentiment"
};

ChatClientAgent actionPlanner = new(
    chatClient,
    "You are a customer support + product triage agent. Based on the conversation so far, output JSON only: { \"priority\": \"p0\"|\"p1\"|\"p2\", \"actions\": string[], \"owner\": \"support\"|\"engineering\"|\"product\", \"replyToCustomer\": string }."
)
{
    Id = "ActionPlanner"
};

Workflow workflow = AgentWorkflowBuilder.BuildSequential(new AIAgent[]
{
    summarizer,
    sentiment,
    actionPlanner
});

Console.WriteLine("Enter a piece of customer feedback. End with an empty line.");
string feedback = ReadMultilineFromStdin();
if (string.IsNullOrWhiteSpace(feedback))
{
    Console.WriteLine("No input provided.");
    return;
}

var messages = new List<ChatMessage>
{
    new(ChatRole.User, feedback)
};

StreamingRun run = await InProcessExecution.StreamAsync(workflow, messages);
await run.TrySendMessageAsync(new TurnToken(emitEvents: true));

Console.WriteLine();
Console.WriteLine("Streaming updates:");

List<ChatMessage> finalConversation = new();
await foreach (WorkflowEvent evt in run.WatchStreamAsync().ConfigureAwait(false))
{
    if (evt is AgentResponseUpdateEvent update)
    {
        Console.WriteLine($"[{update.ExecutorId}] {update.Data}");
    }
    else if (evt is WorkflowOutputEvent outputEvt)
    {
        finalConversation = (List<ChatMessage>)(outputEvt.Data ?? new List<ChatMessage>());
        break;
    }
}

Console.WriteLine();
Console.WriteLine("Final conversation:");
foreach (ChatMessage message in finalConversation)
{
    Console.WriteLine($"- {message.Role}: {message.Content}");
}

static string GetRequiredSetting(IConfiguration config, params string[] keys)
{
    foreach (string key in keys)
    {
        string? value = config[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    throw new InvalidOperationException($"Missing required configuration. Set one of: {string.Join(", ", keys)}");
}

static string? GetOptionalSetting(IConfiguration config, params string[] keys)
{
    foreach (string key in keys)
    {
        string? value = config[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }
    }

    return null;
}

static string ReadMultilineFromStdin()
{
    var builder = new StringBuilder();
    while (true)
    {
        string? line = Console.ReadLine();
        if (line is null || line.Length == 0)
        {
            break;
        }

        builder.AppendLine(line);
    }

    return builder.ToString().Trim();
}
