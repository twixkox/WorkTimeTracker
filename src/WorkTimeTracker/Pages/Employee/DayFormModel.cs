using System.ComponentModel.DataAnnotations;

namespace WorkTimeTracker.Pages.Employee;

public class EntryInput
{
    public int? BoatId { get; set; }

    [Range(0.01, 24, ErrorMessage = "Время затрат — от 0.01 до 24 ч.")]
    public decimal? Hours { get; set; }

    public string? WorkDescription { get; set; }

    public bool IsInhouse { get; set; }
}

/// <summary>Форма рабочего дня (используется и на странице дня, и в недельном виде).</summary>
public class DayFormModel
{
    public DateOnly Date { get; set; }
    public TimeOnly StartTime { get; set; }
    public TimeOnly EndTime { get; set; }

    public List<EntryInput> Entries { get; set; } = new();

    /// <summary>Куда вернуться после сохранения.</summary>
    public string ReturnUrl { get; set; } = "/Employee/Index";

    /// <summary>Очистить пустые строки и нормализовать записи.</summary>
    public List<EntryInput> EffectiveEntries()
    {
        return Entries
            .Where(e => e.Hours is > 0 || !string.IsNullOrWhiteSpace(e.WorkDescription) || e.BoatId is not null)
            .Select(e =>
            {
                if (e.IsInhouse) e.BoatId = null;
                e.WorkDescription = e.WorkDescription?.Trim() ?? string.Empty;
                return e;
            })
            .ToList();
    }
}
