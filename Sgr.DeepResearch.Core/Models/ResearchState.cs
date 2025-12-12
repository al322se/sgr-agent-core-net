namespace Sgr.DeepResearch.Core.Models;

public enum ResearchStatus
{
    Created,
    Running,
    WaitingForClarification,
    Completed,
    Failed
}

public class ResearchState
{
    public Guid Id { get; set; }
    public string Task { get; set; } = string.Empty;
    public ResearchStatus Status { get; set; }
    public List<ChatMessage> History { get; set; } = new();
    public int IterationCount { get; set; }
    public Dictionary<string, string> Sources { get; set; } = new(); // Url -> Content
    public string? FinalReport { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastUpdatedAt { get; set; } = DateTime.UtcNow;
}

public class ChatMessage
{
    public string Role { get; set; } = "user"; // system, user, assistant, tool
    public string Content { get; set; } = string.Empty;
    public string? ToolCallId { get; set; }
}