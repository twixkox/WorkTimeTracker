using WorkTimeTracker.Models;

namespace WorkTimeTracker.Pages.Employee;

/// <summary>Модель частичного представления формы рабочего дня.</summary>
public class DayFormPartialModel
{
    public required DayFormModel Day { get; init; }
    public required IReadOnlyList<Boat> Boats { get; init; }
    public required IReadOnlyList<WorkType> WorkTypes { get; init; }
    public bool CanEdit { get; init; } = true;
    public WorkDay? ExistingDay { get; init; }
    public bool ShowActions { get; init; } = true;
    public bool IsTimeOnly { get; init; }
    public bool RequiresApproval { get; init; } = true;
}
