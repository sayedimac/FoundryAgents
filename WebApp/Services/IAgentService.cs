using WebApp.Models;

namespace WebApp.Services;

public interface IAgentService
{
    Task InitializeAsync();
    IAsyncEnumerable<AgentStreamUpdate> GetStreamingResponseAsync(string agentName, string prompt, IEnumerable<ChatMessage> history, CancellationToken cancellationToken = default);
    Task<AgentInfo> GetAgentInfoAsync(string agentName);
    Task<IEnumerable<AgentInfo>> GetAvailableAgentsAsync();
}
