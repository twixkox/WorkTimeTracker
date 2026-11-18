namespace WorkTimeTracker.Models;

public enum EngineeringTaskPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Urgent = 3
}

public enum EngineeringTaskStatus
{
    New = 0,
    Assigned = 1,
    InProgress = 2,
    Done = 3
}

public class EngineeringTask
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string CreatedByUserId { get; set; } = string.Empty;
    public string CreatedByName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public EngineeringTaskPriority Priority { get; set; } = EngineeringTaskPriority.Normal;
    public EngineeringTaskStatus Status { get; set; } = EngineeringTaskStatus.New;
    public string? AssigneeId { get; set; }
    public ApplicationUser? Assignee { get; set; }
    public DateOnly? StartedOn { get; set; }
    public DateOnly? PlannedDueOn { get; set; }
    public DateOnly? CompletedOn { get; set; }
    public DateOnly? Deadline { get; set; }
    public DateOnly? DeadlineReminderSentOn { get; set; }
    public DateOnly? OverdueReminderSentOn { get; set; }
    public int? BoatId { get; set; }
    public Boat? Boat { get; set; }
    public string? HullNumber { get; set; }
}
