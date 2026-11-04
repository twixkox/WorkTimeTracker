using System.ComponentModel.DataAnnotations;

namespace WorkTimeTracker.Models;

/// <summary>Запись о затратах времени внутри рабочего дня: № катера, часы, выполненные работы.</summary>
public class TimeEntry
{
    public int Id { get; set; }

    public int WorkDayId { get; set; }
    public WorkDay? WorkDay { get; set; }

    /// <summary>№ катера (не требуется, если IsInhouse).</summary>
    public int? BoatId { get; set; }
    public Boat? Boat { get; set; }

    /// <summary>Время затрат, часов (дробное).</summary>
    [Range(0.01, 24)]
    public decimal Hours { get; set; }

    /// <summary>Выполненные работы (текст).</summary>
    public string WorkDescription { get; set; } = string.Empty;

    /// <summary>Внутрицеховые работы — № катера не требуется.</summary>
    public bool IsInhouse { get; set; }
}
