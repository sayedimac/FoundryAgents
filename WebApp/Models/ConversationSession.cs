namespace WebApp.Models;

public class ConversationSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string? CurrentAgentName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastActivityAt { get; set; } = DateTime.UtcNow;
    public List<ChatMessage> Messages { get; set; } = [];
}
