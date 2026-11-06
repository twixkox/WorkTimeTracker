using System.ComponentModel.DataAnnotations.Schema;

namespace WorkTimeTracker.Models;

/// <summary>Статус табеля (рабочего дня).</summary>
public enum TimeSheetStatus
{
    /// <summary>Черновик — редактируется сотрудником.</summary>
    Draft = 0,
    /// <summary>На утверждении у мастера участка.</summary>
    Submitted = 1,
    /// <summary>Утверждён мастером, начальником производства или администратором.</summary>
    Approved = 2,
    /// <summary>Отклонён с комментарием, сотрудник дорабатывает.</summary>
    Rejected = 3,
    /// <summary>Сохранён без утверждения: личное время мастера или режим «только время».</summary>
    Recorded = 4,
}

/// <summary>Рабочий день сотрудника (одна строка табеля).</summary>
public class WorkDay
{
    public int Id { get; set; }

    public int EmployeeId { get; set; }
    public Employee? Employee { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public TimeSheetStatus Status { get; set; } = TimeSheetStatus.Draft;

    /// <summary>Причина отклонения (видит сотрудник).</summary>
    public string? RejectComment { get; set; }

    public DateTime? SubmittedAt { get; set; }
    public DateTime? ApprovedAt { get; set; }
    public string? ApprovedByUserId { get; set; }
    public string? ApprovedByName { get; set; }

    /// <summary>Историческое поле прежней двухэтапной схемы; новые табели его не используют.</summary>
    public bool FinalApproved { get; set; }

    public ICollection<TimeEntry> TimeEntries { get; set; } = new List<TimeEntry>();

    [NotMapped]
    public decimal WorkedHours => Services.TimeCalculationService.WorkedHours(StartTime, EndTime);

    [NotMapped]
    public decimal EntriesHours => Math.Round(TimeEntries.Sum(e => e.Hours), 2);
}
