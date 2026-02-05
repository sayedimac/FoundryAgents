namespace WebApp.Models;

public abstract record AgentStreamUpdate;
public record TextDeltaUpdate(string Text) : AgentStreamUpdate;
public record ToolCallUpdate(string ToolName, string Arguments) : AgentStreamUpdate;
public record ToolResultUpdate(string ToolName, string Result) : AgentStreamUpdate;
