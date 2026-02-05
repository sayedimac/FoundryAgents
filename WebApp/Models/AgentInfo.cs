namespace WebApp.Models;

public class AgentInfo
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Avatar { get; set; } = "";
    public List<string> Capabilities { get; set; } = [];
    public bool HasTools { get; set; }
}
