using System.Collections.Concurrent;
using WebApp.Models;

namespace WebApp.Services;

public class ConversationService : IConversationService
{
    private readonly ConcurrentDictionary<string, ConversationSession> _sessions = new();
    private readonly ILogger<ConversationService> _logger;

    public ConversationService(ILogger<ConversationService> logger)
    {
        _logger = logger;
    }

    public Task<string> CreateSessionAsync()
    {
        var session = new ConversationSession();
        _sessions[session.Id] = session;
        _logger.LogInformation("Created session: {SessionId}", session.Id);
        return Task.FromResult(session.Id);
    }

    public Task<ConversationSession?> GetSessionAsync(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var session);
        return Task.FromResult(session);
    }

    public Task AddMessageAsync(string sessionId, ChatMessage message)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Messages.Add(message);
            session.LastActivityAt = DateTime.UtcNow;
            _logger.LogDebug("Added message to session {SessionId}: {Role}", sessionId, message.Role);
        }
        else
        {
            // Create session if it doesn't exist
            var newSession = new ConversationSession { Id = sessionId };
            newSession.Messages.Add(message);
            _sessions[sessionId] = newSession;
            _logger.LogInformation("Created new session {SessionId} and added message", sessionId);
        }

        return Task.CompletedTask;
    }

    public Task<IEnumerable<ChatMessage>> GetMessagesAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            return Task.FromResult<IEnumerable<ChatMessage>>(session.Messages);
        }

        return Task.FromResult<IEnumerable<ChatMessage>>([]);
    }

    public Task ClearSessionAsync(string sessionId)
    {
        if (_sessions.TryGetValue(sessionId, out var session))
        {
            session.Messages.Clear();
            session.LastActivityAt = DateTime.UtcNow;
            _logger.LogInformation("Cleared session: {SessionId}", sessionId);
        }

        return Task.CompletedTask;
    }
}
