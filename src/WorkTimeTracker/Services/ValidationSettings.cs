namespace WorkTimeTracker.Services;

/// <summary>Параметры валидации табеля.</summary>
public record ValidationSettings
{
    public decimal MaxHoursPerDay { get; init; } = 16m;
    public decimal OvertimeThreshold { get; init; } = 8m;
    public bool CheckIntervalOverlap { get; init; } = false;

    public static ValidationSettings Defaults => new();
}

/// <summary>Уровень проблемы при валидации.</summary>
public enum ValidationSeverity
{
    Error,
    Warning,
}

public record ValidationIssue(ValidationSeverity Severity, string Code, string Message);

/// <summary>Результат валидации рабочего дня.</summary>
public class DayValidationResult
{
    public List<ValidationIssue> Issues { get; } = new();
    public bool HasErrors => Issues.Any(i => i.Severity == ValidationSeverity.Error);

    public void Add(ValidationSeverity severity, string code, string message)
        => Issues.Add(new ValidationIssue(severity, code, message));
}
