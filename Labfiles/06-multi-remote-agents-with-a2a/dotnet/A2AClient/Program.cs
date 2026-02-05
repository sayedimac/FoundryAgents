using System.Text;
using A2A;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.A2A;
using Microsoft.Extensions.Configuration;

Console.OutputEncoding = Encoding.UTF8;

Console.WriteLine("Microsoft Learn Agent - Lab 06 (Remote agents with A2A)");
Console.WriteLine("-----------------------------------------------------");

IConfiguration config = new ConfigurationBuilder()
	.AddJsonFile("appsettings.Development.json", optional: true)
	.AddEnvironmentVariables()
	.Build();

string baseUrl = config["A2A:HostBaseUrl"] ?? "http://localhost:5000";
string titlePath = config["A2A:TitleAgentPath"] ?? "/a2a/title";
string outlinePath = config["A2A:OutlineAgentPath"] ?? "/a2a/outline";
string routerPath = config["A2A:RouterAgentPath"] ?? "/a2a/router";

var titleAgent = new A2AClient(new Uri(new Uri(baseUrl), titlePath)).AsAIAgent();
var outlineAgent = new A2AClient(new Uri(new Uri(baseUrl), outlinePath)).AsAIAgent();
var routerAgent = new A2AClient(new Uri(new Uri(baseUrl), routerPath)).AsAIAgent();

Console.WriteLine("Describe what you want to write (e.g., 'Write a title for a doc about A2A in .NET').");
Console.Write("Request: ");
string? request = Console.ReadLine();
if (string.IsNullOrWhiteSpace(request))
{
	Console.WriteLine("No input provided.");
	return;
}

Console.WriteLine();
Console.WriteLine("Routing...");
string routingJson = await routerAgent.RunAsync(request);
Console.WriteLine(routingJson);

Console.WriteLine();
Console.WriteLine("Calling specialist agent...");

// Simple heuristic: use the model's route field if present.
string specialistOutput;
if (routingJson.Contains("\"route\"", StringComparison.OrdinalIgnoreCase)
	&& routingJson.Contains("outline", StringComparison.OrdinalIgnoreCase))
{
	specialistOutput = await outlineAgent.RunAsync(request);
	Console.WriteLine("[outline]");
}
else
{
	specialistOutput = await titleAgent.RunAsync(request);
	Console.WriteLine("[title]");
}

Console.WriteLine(specialistOutput);
