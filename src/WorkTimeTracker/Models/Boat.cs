namespace WorkTimeTracker.Models;

/// <summary>Справочник катеров.</summary>
public class Boat
{
    public int Id { get; set; }
    public string Number { get; set; } = string.Empty;
    public string? Name { get; set; }
    public string? TrailerNumber { get; set; }
    public string? BoardNumber { get; set; }
    public string? Customer { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();
}
