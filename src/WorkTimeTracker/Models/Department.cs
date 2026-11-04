namespace WorkTimeTracker.Models;

/// <summary>Справочник подразделений (участков).</summary>
public class Department
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    /// <summary>Недельный вид табеля (как у подразделения «15»): 7 блоков «Учёт затрат» по дням недели.</summary>
    public bool IsWeeklyView { get; set; }
    public bool IsActive { get; set; } = true;

    public ICollection<Employee> Employees { get; set; } = new List<Employee>();
}
