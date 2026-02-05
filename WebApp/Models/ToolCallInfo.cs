namespace WebApp.Models;

public class ToolCallInfo
{
    public string ToolName { get; set; } = "";
    public string Arguments { get; set; } = "";
    public string? Result { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}
