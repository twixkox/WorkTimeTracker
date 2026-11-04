namespace WorkTimeTracker.Models;

/// <summary>Типовые описания для поля «Выполненные работы» (опциональный справочник).</summary>
public class WorkType
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
