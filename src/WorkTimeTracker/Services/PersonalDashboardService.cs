using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public record DashboardWorkDayItem(
    int DayId,
    int EmployeeId,
    string EmployeeName,
    string DepartmentName,
    DateOnly Date,
    TimeSheetStatus Status,
    DateTime? SubmittedAt,
    string? RejectComment);

public record DashboardTaskItem(
    int Id,
    string Title,
    EngineeringTaskPriority Priority,
    EngineeringTaskStatus Status,
    string? AssigneeName,
    DateOnly? Deadline,
    bool IsOverdue,
    bool IsDueSoon);

public class PersonalDashboardData
{
    public int EmployeeId { get; init; }
    public string EmployeeName { get; init; } = string.Empty;
    public string DepartmentName { get; init; } = string.Empty;
    public bool IsWeeklyView { get; init; }
    public decimal MonthHours { get; init; }
    public List<DashboardWorkDayItem> PendingDays { get; init; } = new();
    public List<DashboardWorkDayItem> RejectedDays { get; init; } = new();
    public List<DateOnly> MissingDays { get; init; } = new();
    public string? ContinueUrl { get; init; }

    public string DayUrl(DateOnly date) => IsWeeklyView
        ? $"/Employee/Week?week={date:yyyy-MM-dd}"
        : $"/Employee/Day?date={date:yyyy-MM-dd}";
}

public class SectionDashboardData
{
    public int DepartmentId { get; init; }
    public string DepartmentName { get; init; } = string.Empty;
    public int OverdueCount { get; init; }
    public List<DashboardWorkDayItem> Queue { get; init; } = new();
    public List<string> MissingEmployees { get; init; } = new();
}

public class ProductionDashboardData
{
    public int ApprovedDays { get; init; }
    public decimal MonthHours { get; init; }
    public int RejectedDays { get; init; }
    public int OverdueCount { get; init; }
    public int ProblemCount => RejectedDays + OverdueCount + MissingEmployees.Count;
    public List<DashboardWorkDayItem> Queue { get; init; } = new();
    public List<string> MissingEmployees { get; init; } = new();
}

public class EngineeringDashboardData
{
    public bool AssignedOnly { get; init; }
    public int ActiveCount { get; init; }
    public int OverdueCount { get; init; }
    public int DueSoonCount { get; init; }
    public int UnassignedCount { get; init; }
    public List<DashboardTaskItem> Tasks { get; init; } = new();
}

public class PersonalDashboardViewModel
{
    public DateOnly Today { get; init; }
    public PersonalDashboardData? Personal { get; init; }
    public SectionDashboardData? Section { get; init; }
    public ProductionDashboardData? Production { get; init; }
    public EngineeringDashboardData? Engineering { get; init; }
    public bool ShowPersonalLink { get; init; }
    public bool ShowSectionLink { get; init; }
    public bool ShowProductionLink { get; init; }
}

public class PersonalDashboardService
{
    private static readonly TimeSpan ApprovalWaitingLimit = TimeSpan.FromHours(24);
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;

    public PersonalDashboardService(ApplicationDbContext db, CurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<PersonalDashboardViewModel> GetDashboardAsync(DateTime? now = null,
        CancellationToken cancellationToken = default)
    {
        var currentTime = now ?? DateTime.Now;
        var today = DateOnly.FromDateTime(currentTime);
        var showPersonal = _currentUser.IsInRole(AppRoles.Employee)
            || _currentUser.IsInRole(AppRoles.SectionMaster);
        var showSection = _currentUser.IsInRole(AppRoles.SectionMaster);
        var showProduction = _currentUser.IsInRole(AppRoles.ProductionHead)
            || _currentUser.IsInRole(AppRoles.Admin);
        var showEngineering = showProduction
            || _currentUser.IsInRole(AppRoles.ChiefEngineer)
            || _currentUser.IsInRole(AppRoles.Engineer);

        var employee = showPersonal || showSection
            ? await _currentUser.GetEmployeeAsync()
            : null;

        return new PersonalDashboardViewModel
        {
            Today = today,
            Personal = showPersonal && employee is not null
                ? await GetPersonalAsync(employee, today, cancellationToken)
                : null,
            Section = showSection && employee is not null
                ? await GetSectionAsync(employee.DepartmentId, employee.Department?.Name ?? string.Empty,
                    currentTime, cancellationToken)
                : null,
            Production = showProduction
                ? await GetProductionAsync(currentTime, cancellationToken)
                : null,
            Engineering = showEngineering
                ? await GetEngineeringAsync(today, cancellationToken)
                : null,
            ShowPersonalLink = showPersonal,
            ShowSectionLink = showSection,
            ShowProductionLink = showProduction,
        };
    }

    private async Task<PersonalDashboardData> GetPersonalAsync(Employee employee, DateOnly today,
        CancellationToken cancellationToken)
    {
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var days = await _db.WorkDays.AsNoTracking()
            .Where(w => w.EmployeeId == employee.Id && w.Date >= from && w.Date <= to)
            .OrderByDescending(w => w.Date)
            .ToListAsync(cancellationToken);
        var filledDates = days.Select(d => d.Date).ToHashSet();
        var missingDays = new List<DateOnly>();
        for (var date = from; date <= today; date = date.AddDays(1))
        {
            if (date.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday
                && !filledDates.Contains(date))
            {
                missingDays.Add(date);
            }
        }
        missingDays.Reverse();

        var continueDate = days
            .Where(d => d.Status == TimeSheetStatus.Rejected)
            .Select(d => (DateOnly?)d.Date)
            .FirstOrDefault()
            ?? days.Where(d => d.Status is TimeSheetStatus.Draft or TimeSheetStatus.Recorded)
                .Select(d => (DateOnly?)d.Date)
                .FirstOrDefault()
            ?? missingDays.Cast<DateOnly?>().FirstOrDefault();

        DashboardWorkDayItem Item(WorkDay day) => new(day.Id, day.EmployeeId, employee.FullName,
            employee.Department?.Name ?? string.Empty, day.Date, day.Status, day.SubmittedAt, day.RejectComment);

        return new PersonalDashboardData
        {
            EmployeeId = employee.Id,
            EmployeeName = employee.FullName,
            DepartmentName = employee.Department?.Name ?? string.Empty,
            IsWeeklyView = employee.Department?.IsWeeklyView == true,
            MonthHours = Math.Round(days.Sum(d => d.WorkedHours), 2),
            PendingDays = days.Where(d => d.Status == TimeSheetStatus.Submitted).Select(Item).ToList(),
            RejectedDays = days.Where(d => d.Status == TimeSheetStatus.Rejected).Select(Item).ToList(),
            MissingDays = missingDays,
            ContinueUrl = continueDate is { } continueDay ? EmployeeDayUrl(employee, continueDay) : null,
        };
    }

    private async Task<SectionDashboardData> GetSectionAsync(int departmentId, string departmentName,
        DateTime now, CancellationToken cancellationToken)
    {
        var queue = await GetApprovalQueueAsync(now, cancellationToken, departmentId);
        return new SectionDashboardData
        {
            DepartmentId = departmentId,
            DepartmentName = departmentName,
            OverdueCount = queue.OverdueCount,
            Queue = queue.Items,
            MissingEmployees = await GetMissingEmployeesAsync(DateOnly.FromDateTime(now), cancellationToken,
                departmentId),
        };
    }

    private async Task<ProductionDashboardData> GetProductionAsync(DateTime now,
        CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now);
        var from = new DateOnly(today.Year, today.Month, 1);
        var to = from.AddMonths(1).AddDays(-1);
        var monthDays = await _db.WorkDays.AsNoTracking()
            .Where(w => w.Date >= from && w.Date <= to)
            .ToListAsync(cancellationToken);
        var queue = await GetApprovalQueueAsync(now, cancellationToken);
        var missingEmployees = await GetMissingEmployeesAsync(today, cancellationToken);

        return new ProductionDashboardData
        {
            ApprovedDays = monthDays.Count(d => d.Status == TimeSheetStatus.Approved),
            MonthHours = Math.Round(monthDays.Sum(d => d.WorkedHours), 2),
            RejectedDays = monthDays.Count(d => d.Status == TimeSheetStatus.Rejected),
            OverdueCount = queue.OverdueCount,
            Queue = queue.Items,
            MissingEmployees = missingEmployees,
        };
    }

    private async Task<(List<DashboardWorkDayItem> Items, int OverdueCount)> GetApprovalQueueAsync(
        DateTime now, CancellationToken cancellationToken, int? departmentId = null)
    {
        var query = _db.WorkDays.AsNoTracking()
            .Include(w => w.Employee).ThenInclude(e => e!.Department)
            .Where(w => w.Status == TimeSheetStatus.Submitted);
        if (departmentId is { } id)
            query = query.Where(w => w.Employee != null && w.Employee.DepartmentId == id);

        var days = await query
            .OrderBy(w => w.SubmittedAt)
            .ThenBy(w => w.Date)
            .ToListAsync(cancellationToken);
        var overdueBefore = now.Subtract(ApprovalWaitingLimit);
        return (days.Select(d => new DashboardWorkDayItem(d.Id, d.EmployeeId,
                d.Employee?.FullName ?? string.Empty, d.Employee?.Department?.Name ?? string.Empty,
                d.Date, d.Status, d.SubmittedAt, d.RejectComment)).ToList(),
            days.Count(d => d.SubmittedAt is { } submittedAt && submittedAt <= overdueBefore));
    }

    private async Task<List<string>> GetMissingEmployeesAsync(DateOnly today,
        CancellationToken cancellationToken, int? departmentId = null)
    {
        if (today.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return new List<string>();

        var filledQuery = _db.WorkDays.AsNoTracking().Where(w => w.Date == today);
        if (departmentId is { } id)
            filledQuery = filledQuery.Where(w => w.Employee != null && w.Employee.DepartmentId == id);
        var filledEmployeeIds = await filledQuery.Select(w => w.EmployeeId).ToListAsync(cancellationToken);

        var employeesQuery = _db.Employees.AsNoTracking()
            .Where(e => e.IsActive && e.Department != null && e.Department.IsActive);
        if (departmentId is { } department)
            employeesQuery = employeesQuery.Where(e => e.DepartmentId == department);
        return await employeesQuery
            .Where(e => !filledEmployeeIds.Contains(e.Id))
            .OrderBy(e => e.FullName)
            .Select(e => e.FullName)
            .ToListAsync(cancellationToken);
    }

    private async Task<EngineeringDashboardData> GetEngineeringAsync(DateOnly today,
        CancellationToken cancellationToken)
    {
        var assignedOnly = _currentUser.IsInRole(AppRoles.Engineer)
            && !_currentUser.IsInRole(AppRoles.ChiefEngineer)
            && !_currentUser.IsInRole(AppRoles.ProductionHead)
            && !_currentUser.IsInRole(AppRoles.Admin);
        var query = _db.EngineeringTasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Where(t => t.Status != EngineeringTaskStatus.Done);
        if (assignedOnly)
            query = query.Where(t => t.AssigneeId == _currentUser.UserId);

        var tasks = await query.ToListAsync(cancellationToken);
        bool IsOverdue(EngineeringTask task) => task.Deadline is { } deadline && deadline < today;
        bool IsDueSoon(EngineeringTask task) => task.Deadline is { } deadline
            && deadline >= today && deadline <= today.AddDays(2);
        var orderedTasks = tasks
            .OrderByDescending(IsOverdue)
            .ThenByDescending(t => t.Priority)
            .ThenBy(t => t.Deadline == null)
            .ThenBy(t => t.Deadline)
            .ThenByDescending(t => t.CreatedAt)
            .Take(12)
            .Select(t => new DashboardTaskItem(t.Id, t.Title, t.Priority, t.Status,
                string.IsNullOrWhiteSpace(t.Assignee?.DisplayName) ? t.Assignee?.Email : t.Assignee.DisplayName,
                t.Deadline, IsOverdue(t), IsDueSoon(t)))
            .ToList();

        return new EngineeringDashboardData
        {
            AssignedOnly = assignedOnly,
            ActiveCount = tasks.Count,
            OverdueCount = tasks.Count(IsOverdue),
            DueSoonCount = tasks.Count(IsDueSoon),
            UnassignedCount = assignedOnly ? 0 : tasks.Count(t => t.AssigneeId == null),
            Tasks = orderedTasks,
        };
    }

    private static string EmployeeDayUrl(Employee employee, DateOnly date)
        => employee.Department?.IsWeeklyView == true
            ? $"/Employee/Week?week={date:yyyy-MM-dd}"
            : $"/Employee/Day?date={date:yyyy-MM-dd}";
}
