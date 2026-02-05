using WebApp.Models;

namespace WebApp.Services;

public interface IConversationService
{
    Task<string> CreateSessionAsync();
    Task<ConversationSession?> GetSessionAsync(string sessionId);
    Task AddMessageAsync(string sessionId, ChatMessage message);
    Task<IEnumerable<ChatMessage>> GetMessagesAsync(string sessionId);
    Task ClearSessionAsync(string sessionId);
}
