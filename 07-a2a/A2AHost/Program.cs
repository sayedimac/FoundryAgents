using A2A.AspNetCore;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Agents.AI.Hosting;
using Microsoft.Extensions.AI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddSwaggerGen();

string endpoint = builder.Configuration["AZURE_OPENAI_ENDPOINT"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_ENDPOINT is not set.");
string deploymentName = builder.Configuration["AZURE_OPENAI_DEPLOYMENT_NAME"]
    ?? throw new InvalidOperationException("AZURE_OPENAI_DEPLOYMENT_NAME is not set.");

IChatClient chatClient = new AzureOpenAIClient(
        new Uri(endpoint),
        new AzureCliCredential())
    .GetChatClient(deploymentName)
    .AsIChatClient();
builder.Services.AddSingleton(chatClient);

var titleAgent = builder.AddAIAgent(
    "title",
    instructions: "You write concise, punchy titles for technical documents. Output only the title text.");

var outlineAgent = builder.AddAIAgent(
    "outline",
    instructions: "You create a structured outline for a technical document. Output a numbered outline only.");

var routingAgent = builder.AddAIAgent(
    "router",
    instructions:
        "You route writing requests to the best specialist agent. " +
        "Respond with JSON only: { \"route\": \"title\"|\"outline\", \"reason\": string }.");

var app = builder.Build();

app.MapOpenApi();
app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/", () => Results.Redirect("/swagger"));

app.MapA2A(titleAgent, path: "/a2a/title", agentCard: new()
{
    Name = "Title Agent",
    Description = "Creates a short, catchy title for a technical document.",
    Version = "1.0"
});

app.MapA2A(outlineAgent, path: "/a2a/outline", agentCard: new()
{
    Name = "Outline Agent",
    Description = "Creates a structured outline for a technical document.",
    Version = "1.0"
});

app.MapA2A(routingAgent, path: "/a2a/router", agentCard: new()
{
    Name = "Routing Agent",
    Description = "Routes requests to title vs outline specialists.",
    Version = "1.0"
});

app.Run();
