using System.Text;
using Microsoft.AspNetCore.SignalR;
using WebApp.Models;
using WebApp.Services;

namespace WebApp.Hubs;

public class ChatHub : Hub
{
    private readonly IAgentService _agentService;
    private readonly IConversationService _conversationService;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(
        IAgentService agentService,
        IConversationService conversationService,
        ILogger<ChatHub> logger)
    {
        _agentService = agentService;
        _conversationService = conversationService;
        _logger = logger;
    }

    public async Task SendMessage(string sessionId, string agentName, string message)
    {
        _logger.LogInformation("Received message for session {SessionId} agent {Agent}: {Message}",
            sessionId, agentName, message[..Math.Min(100, message.Length)]);

        // Store user message
        await _conversationService.AddMessageAsync(sessionId, new ChatMessage
        {
            Role = "user",
            Content = message,
            Timestamp = DateTime.UtcNow
        });

        // Get conversation history for context
        var history = await _conversationService.GetMessagesAsync(sessionId);

        // Stream response from agent
        var messageId = Guid.NewGuid().ToString();
        var fullResponse = new StringBuilder();

        // Resolve GitHub user token from session (for Code agent GitHub MCP calls)
        string? githubToken = null;
        if (agentName is "Code" or "GitHub")
        {
            var httpContext = Context.GetHttpContext();
            githubToken = httpContext?.Session.GetString("github_access_token");
        }

        try
        {
            await foreach (var update in _agentService.GetStreamingResponseAsync(agentName, message, history, githubToken))
            {
                switch (update)
                {
                    case TextDeltaUpdate textDelta:
                        fullResponse.Append(textDelta.Text);
                        await Clients.Caller.SendAsync("ReceiveMessageChunk", messageId, textDelta.Text);
                        break;

                    case ToolCallUpdate toolCall:
                        await Clients.Caller.SendAsync("ReceiveToolCall", messageId, toolCall.ToolName, toolCall.Arguments);
                        break;

                    case ToolResultUpdate toolResult:
                        await Clients.Caller.SendAsync("ReceiveToolResult", messageId, toolResult.ToolName, toolResult.Result);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error streaming response for session {SessionId}", sessionId);
            await Clients.Caller.SendAsync("ReceiveError", messageId, $"Error: {ex.Message}");
        }

        // Store assistant message
        if (fullResponse.Length > 0)
        {
            await _conversationService.AddMessageAsync(sessionId, new ChatMessage
            {
                Role = "assistant",
                Content = fullResponse.ToString(),
                Timestamp = DateTime.UtcNow,
                AgentName = agentName
            });
        }

        await Clients.Caller.SendAsync("MessageComplete", messageId);
    }

    public async Task SelectAgent(string agentName)
    {
        var agentInfo = await _agentService.GetAgentInfoAsync(agentName);
        await Clients.Caller.SendAsync("AgentSelected", agentInfo);
    }

    public async Task CreateSession()
    {
        var sessionId = await _conversationService.CreateSessionAsync();
        await Clients.Caller.SendAsync("SessionCreated", sessionId);
    }

    public async Task ClearSession(string sessionId)
    {
        await _conversationService.ClearSessionAsync(sessionId);
        await Clients.Caller.SendAsync("SessionCleared", sessionId);
    }

    public async Task<IEnumerable<AgentInfo>> GetAvailableAgents()
    {
        return await _agentService.GetAvailableAgentsAsync();
    }

    public override async Task OnConnectedAsync()
    {
        _logger.LogInformation("Client connected: {ConnectionId}", Context.ConnectionId);
        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        await base.OnDisconnectedAsync(exception);
    }
}
