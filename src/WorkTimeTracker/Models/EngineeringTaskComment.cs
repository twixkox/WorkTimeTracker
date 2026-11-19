namespace WorkTimeTracker.Models;

public class EngineeringTaskComment
{
    public int Id { get; set; }
    public int EngineeringTaskId { get; set; }
    public EngineeringTask EngineeringTask { get; set; } = null!;
    public string AuthorUserId { get; set; } = string.Empty;
    public string AuthorName { get; set; } = string.Empty;
    public string Text { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
