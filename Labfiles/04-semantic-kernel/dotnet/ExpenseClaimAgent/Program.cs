using System.Text;
using System.Text.Json;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.Agents.AI;
using Microsoft.Extensions.Configuration;
using OpenAI;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("Microsoft Learn Agent - Lab 04 (Expense claim agent / Agent Framework)");
Console.WriteLine("---------------------------------------------------------------");

IConfiguration config = new ConfigurationBuilder()
    .AddJsonFile("appsettings.Development.json", optional: true)
    .AddEnvironmentVariables()
    .Build();

string endpoint = GetRequiredSetting(config, "AZURE_OPENAI_ENDPOINT", "Azure:OpenAIEndpoint");
string deploymentName = GetRequiredSetting(config, "AZURE_OPENAI_DEPLOYMENT_NAME", "Azure:OpenAIDeploymentName");
string? apiKey = GetOptionalSetting(config, "AZURE_OPENAI_KEY", "Azure:OpenAIKey");

AzureOpenAIClient client = !string.IsNullOrWhiteSpace(apiKey)
    ? new AzureOpenAIClient(new Uri(endpoint), new AzureKeyCredential(apiKey))
    : new AzureOpenAIClient(new Uri(endpoint), new AzureCliCredential());

AIAgent agent = client
    .GetChatClient(deploymentName)
    .AsAIAgent(instructions:
        "You are an expense-claim assistant. Your job is to extract a structured expense claim and recommend a decision. " +
        "Return ONLY valid JSON with no markdown fences.\n\n" +
        "Output schema:\n" +
        "{\n" +
        "  \"employee\": string,\n" +
        "  \"date\": string (YYYY-MM-DD if possible),\n" +
        "  \"amount\": number,\n" +
        "  \"currency\": string,\n" +
        "  \"category\": \"meal\"|\"travel\"|\"lodging\"|\"supplies\"|\"other\",\n" +
        "  \"description\": string,\n" +
        "  \"receiptProvided\": boolean,\n" +
        "  \"recommendedDecision\": \"approve\"|\"needs_review\"|\"reject\",\n" +
        "  \"reasons\": string[]\n" +
        "}\n\n" +
        "Rules of thumb:\n" +
        "- Meals > 50 USD equivalent: needs_review\n" +
        "- Lodging > 250 USD equivalent: needs_review\n" +
        "- Travel > 500 USD equivalent: needs_review\n" +
        "- If receiptProvided is false and amount > 25: needs_review\n" +
        "- Fraud indicators: reject")
    ;

Console.WriteLine("Paste an expense claim in plain English. End with an empty line.");
string claimText = ReadMultilineFromStdin();

if (string.IsNullOrWhiteSpace(claimText))
{
    Console.WriteLine("No input provided.");
    return;
}

string modelJson = await agent.RunAsync(
    "Extract the expense claim and decide. Input:\n" + claimText);

Console.WriteLine();
Console.WriteLine("Model output (JSON):");
Console.WriteLine(modelJson);

Console.WriteLine();
Console.WriteLine("Local policy check:");
try
{
    ExpenseClaimDecision decision = JsonSerializer.Deserialize<ExpenseClaimDecision>(
        modelJson,
        new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
        ?? throw new JsonException("Model output JSON deserialized to null.");

    PolicyResult policyResult = EvaluatePolicy(decision);

    Console.WriteLine($"- Parsed category: {decision.Category}");
    Console.WriteLine($"- Parsed amount: {decision.Amount} {decision.Currency}");
    Console.WriteLine($"- Receipt: {decision.ReceiptProvided}");
    Console.WriteLine($"- Recommended (model): {decision.RecommendedDecision}");
    Console.WriteLine($"- Recommended (policy): {policyResult.RecommendedDecision}");
    foreach (string reason in policyResult.Reasons)
    {
        Console.WriteLine($"  - {reason}");
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Could not parse/validate model JSON: {ex.Message}");
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

static PolicyResult EvaluatePolicy(ExpenseClaimDecision decision)
{
    var reasons = new List<string>();
    string recommendedDecision = "approve";

    // Treat unknown/unsupported currency as needs_review.
    bool isUsd = string.Equals(decision.Currency, "USD", StringComparison.OrdinalIgnoreCase);
    if (!isUsd)
    {
        recommendedDecision = "needs_review";
        reasons.Add("Non-USD currency: manual conversion required.");
    }

    if (!decision.ReceiptProvided && decision.Amount > 25)
    {
        recommendedDecision = "needs_review";
        reasons.Add("Receipt missing for amount > 25.");
    }

    double amountUsd = decision.Amount;
    if (decision.Category is null)
    {
        recommendedDecision = "needs_review";
        reasons.Add("Missing category.");
        return new PolicyResult(recommendedDecision, reasons);
    }

    switch (decision.Category.ToLowerInvariant())
    {
        case "meal":
            if (amountUsd > 50)
            {
                recommendedDecision = "needs_review";
                reasons.Add("Meal exceeds 50 USD.");
            }
            break;
        case "lodging":
            if (amountUsd > 250)
            {
                recommendedDecision = "needs_review";
                reasons.Add("Lodging exceeds 250 USD.");
            }
            break;
        case "travel":
            if (amountUsd > 500)
            {
                recommendedDecision = "needs_review";
                reasons.Add("Travel exceeds 500 USD.");
            }
            break;
        case "supplies":
        case "other":
            if (amountUsd > 200)
            {
                recommendedDecision = "needs_review";
                reasons.Add("High amount for category; review needed.");
            }
            break;
        default:
            recommendedDecision = "needs_review";
            reasons.Add($"Unknown category '{decision.Category}'.");
            break;
    }

    if (reasons.Count == 0)
    {
        reasons.Add("Within policy thresholds.");
    }

    return new PolicyResult(recommendedDecision, reasons);
}

sealed record ExpenseClaimDecision(
    string Employee,
    string Date,
    double Amount,
    string Currency,
    string Category,
    string Description,
    bool ReceiptProvided,
    string RecommendedDecision,
    string[] Reasons);

sealed record PolicyResult(string RecommendedDecision, List<string> Reasons);
