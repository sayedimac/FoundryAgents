using WebApp.Models;

namespace WebApp.Services;

public interface IAgentService
{
    Task InitializeAsync();
    IAsyncEnumerable<AgentStreamUpdate> GetStreamingResponseAsync(string agentName, string prompt, IEnumerable<ChatMessage> history, IReadOnlyList<string>? imagePaths = null, string? githubToken = null, CancellationToken cancellationToken = default);
    Task<AgentInfo> GetAgentInfoAsync(string agentName);
    Task<IEnumerable<AgentInfo>> GetAvailableAgentsAsync();
}
