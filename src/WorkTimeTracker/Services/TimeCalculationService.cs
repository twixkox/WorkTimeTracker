using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

/// <summary>
/// Чистая логика расчётов и валидации рабочего дня — без зависимостей от БД, покрыта модульными тестами.
/// </summary>
public static class TimeCalculationService
{
    /// <summary>Отработанные часы = окончание − начало (0, если окончание раньше начала).</summary>
    public static decimal WorkedHours(TimeOnly start, TimeOnly end)
        => end <= start ? 0m : Math.Round((decimal)(end - start).TotalHours, 2);

    /// <summary>Сумма часов по записям затрат.</summary>
    public static decimal EntriesHours(IEnumerable<TimeEntry> entries)
        => Math.Round(entries.Sum(e => e.Hours), 2);

    /// <summary>Переработка, часов (0, если нет).</summary>
    public static decimal OvertimeHours(TimeOnly start, TimeOnly end, decimal threshold)
        => Math.Max(0m, WorkedHours(start, end) - threshold);

    /// <summary>
    /// Валидация рабочего дня.
    /// strict: true — ошибки блокируют отправку на утверждение; false — только предупреждения (черновик).
    /// </summary>
    public static DayValidationResult Validate(
        TimeOnly start, TimeOnly end, IList<TimeEntry> entries, ValidationSettings settings, bool strict)
    {
        var result = new DayValidationResult();
        var severity = strict ? ValidationSeverity.Error : ValidationSeverity.Warning;
        var worked = WorkedHours(start, end);
        var sum = EntriesHours(entries);

        if (end <= start)
            result.Add(ValidationSeverity.Error, "time-order", "Окончание рабочего дня должно быть позже начала.");

        if (worked > settings.MaxHoursPerDay)
            result.Add(severity, "max-hours",
                $"Отработанное время {worked:0.##} ч превышает допустимый максимум {settings.MaxHoursPerDay:0.##} ч.");

        if (worked > settings.OvertimeThreshold)
            result.Add(ValidationSeverity.Warning, "overtime",
                $"Переработка: отработано {worked:0.##} ч (порог {settings.OvertimeThreshold:0.##} ч).");

        if (sum > worked)
            result.Add(severity, "sum-exceeds",
                $"Сумма затрат {sum:0.##} ч превышает отработанное время {worked:0.##} ч.");

        if (entries.Any(e => e.Hours <= 0))
            result.Add(ValidationSeverity.Error, "hours-positive", "Время затрат должно быть больше нуля.");

        if (entries.Any(e => !e.IsInhouse && e.BoatId is null))
            result.Add(ValidationSeverity.Error, "boat-required", "Для записи без флага «внутрицеховые» укажите № катера.");

        if (entries.Any(e => e.IsInhouse && e.BoatId is not null))
            result.Add(ValidationSeverity.Warning, "boat-ignored",
                "Для внутрицеховых работ № катера не требуется — значение будет проигнорировано.");

        if (entries.Any(e => string.IsNullOrWhiteSpace(e.WorkDescription)))
            result.Add(ValidationSeverity.Warning, "work-empty", "Есть записи без описания выполненных работ.");

        var duplicates = entries
            .Where(e => e.BoatId is not null)
            .GroupBy(e => new { e.BoatId, e.WorkDescription })
            .Where(g => g.Count() > 1);
        if (duplicates.Any())
            result.Add(ValidationSeverity.Warning, "duplicate", "Есть повторяющиеся записи по одному катеру с одинаковым описанием.");

        return result;
    }

    /// <summary>
    /// Проверка пересечения временных интервалов между рабочими днями сотрудника (опционально).
    /// </summary>
    public static bool HasOverlap(DateOnly date, TimeOnly start, TimeOnly end, IEnumerable<(DateOnly Date, TimeOnly Start, TimeOnly End)> others)
        => others.Any(o => o.Date == date && start < o.End && end > o.Start);
}
