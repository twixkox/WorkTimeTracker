namespace WorkTimeTracker.Models;

/// <summary>Сотрудник производства. Может быть привязан к учётной записи ASP.NET Core Identity.</summary>
public class Employee
{
    public int Id { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string? Position { get; set; }

    public int DepartmentId { get; set; }
    public Department? Department { get; set; }

    /// <summary>Идентификатор учётной записи Identity (null — запись не привязана).</summary>
    public string? UserId { get; set; }

    public bool IsActive { get; set; } = true;

    public ICollection<WorkDay> WorkDays { get; set; } = new List<WorkDay>();
}
