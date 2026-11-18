namespace WorkTimeTracker.Models;

public class EngineeringTask
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }
    public DateOnly? StartedOn { get; set; }
    public DateOnly? PlannedDueOn { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public DateOnly? Deadline { get; set; }
    public string? HullNumber { get; set; }
}
