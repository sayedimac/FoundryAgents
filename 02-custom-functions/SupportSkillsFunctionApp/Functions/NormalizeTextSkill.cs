using System.Net;
using System.Text.Json;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace SupportSkillsFunctionApp.Functions;

public sealed class NormalizeTextSkill
{
    private readonly ILogger _logger;

    public NormalizeTextSkill(ILoggerFactory loggerFactory)
    {
        _logger = loggerFactory.CreateLogger<NormalizeTextSkill>();
    }

    [Function("normalize-text")]
    public async Task<HttpResponseData> RunAsync(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "normalize-text")] HttpRequestData req)
    {
        using var reader = new StreamReader(req.Body);
        var body = await reader.ReadToEndAsync();

        if (string.IsNullOrWhiteSpace(body))
        {
            var empty = req.CreateResponse(HttpStatusCode.BadRequest);
            await empty.WriteStringAsync("Request body is required.");
            return empty;
        }

        // Azure AI Search custom skill format (Web API skill): { values: [ { recordId, data: { text } } ] }
        // Also supports a simple format: { text: "..." }
        using var doc = JsonDocument.Parse(body);

        if (doc.RootElement.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            var response = new
            {
                values = values.EnumerateArray().Select(v =>
                {
                    var recordId = v.TryGetProperty("recordId", out var rid) ? rid.GetString() : "";
                    var text = v.TryGetProperty("data", out var data) && data.TryGetProperty("text", out var t)
                        ? t.GetString() ?? string.Empty
                        : string.Empty;

                    var normalized = Normalize(text);

                    return new
                    {
                        recordId,
                        data = new
                        {
                            normalizedText = normalized
                        }
                    };
                })
            };

            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(response);
            return ok;
        }

        if (doc.RootElement.TryGetProperty("text", out var textElement))
        {
            var normalized = Normalize(textElement.GetString() ?? string.Empty);
            var ok = req.CreateResponse(HttpStatusCode.OK);
            await ok.WriteAsJsonAsync(new { normalizedText = normalized });
            return ok;
        }

        _logger.LogWarning("Unknown request schema.");
        var bad = req.CreateResponse(HttpStatusCode.BadRequest);
        await bad.WriteStringAsync("Unsupported request schema. Provide either { text: '...' } or { values: [...] }.");
        return bad;
    }

    private static string Normalize(string input)
    {
        return string.Join(
                " ",
                input
                    .Trim()
                    .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            .ToLowerInvariant();
    }
}
