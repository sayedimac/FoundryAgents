namespace WebApp.Models;

public class ChatMessage
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Role { get; set; } = "user";
    public string Content { get; set; } = "";
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string? AgentName { get; set; }
    public List<FileAttachment> Attachments { get; set; } = [];
    public List<ToolCallInfo> ToolCalls { get; set; } = [];
}
