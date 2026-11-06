using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Pages.Employee;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Master;

public class DayModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;

    public DayModel(ApplicationDbContext db, CurrentUserService currentUser, SettingsService settings,
        AuditService audit, NotificationService notifications)
    {
        _db = db;
        _currentUser = currentUser;
        _settings = settings;
        _audit = audit;
        _notifications = notifications;
    }

    [BindProperty]
    public DayFormModel Input { get; set; } = new();

    [BindProperty]
    public string? RejectComment { get; set; }

    public Models.Employee? Employee { get; private set; }
    public WorkDay? Day { get; private set; }
    public List<Boat> Boats { get; private set; } = new();
    public List<WorkType> WorkTypes { get; private set; } = new();
    public bool CanEdit { get; private set; }
    public List<ValidationIssue> Issues { get; private set; } = new();
    public bool IsSectionMaster { get; private set; }

    public async Task<IActionResult> OnGet(int employeeId, DateOnly date)
    {
        if (!await LoadAsync(employeeId, date)) return Forbid();
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync(int employeeId, DateOnly date) => SaveAsync(employeeId, date);

    public async Task<IActionResult> OnPostApproveAsync(int employeeId, DateOnly date)
    {
        if (!await LoadAsync(employeeId, date)) return Forbid();
        if (Day is null or { Id: 0 } || Employee is null) return NotFound();

        if (IsSectionMaster && Employee.DepartmentId != (await _currentUser.GetEmployeeAsync())?.DepartmentId)
            return Forbid();

        if (WorkflowService.TryApprove(Day, _currentUser.UserId ?? "", _currentUser.UserName))
        {
            await _db.SaveChangesAsync();
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Approved", "WorkDay", Day.Id.ToString(),
                $"{Employee.FullName}, {Day.Date:dd.MM.yyyy}");
            if (Employee.UserId is not null)
                await _notifications.NotifyAsync(Employee.UserId, "Табель утверждён",
                    $"Ваш табель за {Day.Date:dd.MM.yyyy} утверждён.", "/Employee/History");
            TempData["Success"] = "Табель утверждён.";
        }
        else
        {
            TempData["Error"] = "Недопустимый переход статуса.";
        }
        return Redirect($"/Master/Day?employeeId={employeeId}&date={date:yyyy-MM-dd}");
    }

    public async Task<IActionResult> OnPostRejectAsync(int employeeId, DateOnly date)
    {
        if (!await LoadAsync(employeeId, date)) return Forbid();
        if (Day is null or { Id: 0 } || Employee is null) return NotFound();

        if (IsSectionMaster && Employee.DepartmentId != (await _currentUser.GetEmployeeAsync())?.DepartmentId)
            return Forbid();

        if (string.IsNullOrWhiteSpace(RejectComment))
        {
            Issues = new List<ValidationIssue> { new(ValidationSeverity.Error, "comment", "Укажите причину отклонения.") };
            CanEdit = true;
            return Page();
        }

        if (WorkflowService.TryReject(Day, RejectComment))
        {
            await _db.SaveChangesAsync();
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Rejected", "WorkDay", Day.Id.ToString(),
                $"{Employee.FullName}, {Day.Date:dd.MM.yyyy}: {RejectComment}");
            if (Employee.UserId is not null)
                await _notifications.NotifyAsync(Employee.UserId, "Табель отклонён",
                    $"Табель за {Day.Date:dd.MM.yyyy} отклонён: {RejectComment}", $"/Employee/Day?date={Day.Date:yyyy-MM-dd}");
            TempData["Success"] = "Табель отклонён с комментарием.";
        }
        return Redirect($"/Master/Day?employeeId={employeeId}&date={date:yyyy-MM-dd}");
    }

    private async Task<IActionResult> SaveAsync(int employeeId, DateOnly date)
    {
        if (!await LoadAsync(employeeId, date)) return Forbid();
        if (Day is null || Employee is null) return NotFound();
        if (!WorkflowService.CanMasterEdit(Day.Status) && !(_currentUser.IsInRole(AppRoles.Admin) || _currentUser.IsInRole(AppRoles.ProductionHead)))
            return Forbid();

        Day.StartTime = Input.StartTime;
        Day.EndTime = Input.EndTime;

        var isTimeOnly = Employee.Department?.IsTimeOnly == true;
        var settings = await _settings.GetValidationSettingsAsync();
        var entries = isTimeOnly ? new List<EntryInput>() : Input.EffectiveEntries();
        var validation = TimeCalculationService.Validate(Input.StartTime, Input.EndTime,
            entries.Select(e => new TimeEntry
            {
                BoatId = e.IsInhouse ? null : e.BoatId,
                Hours = e.Hours ?? 0,
                WorkDescription = e.WorkDescription ?? string.Empty,
                IsInhouse = e.IsInhouse,
            }).ToList(), settings, strict: !isTimeOnly);

        if (validation.HasErrors)
        {
            Issues = validation.Issues;
            CanEdit = true;
            return Page();
        }

        Day.TimeEntries.Clear();
        foreach (var e in entries)
        {
            Day.TimeEntries.Add(new TimeEntry
            {
                BoatId = e.IsInhouse ? null : e.BoatId,
                Hours = e.Hours ?? 0,
                WorkDescription = e.WorkDescription ?? string.Empty,
                IsInhouse = e.IsInhouse,
            });
        }
        await _db.SaveChangesAsync();
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Updated", "WorkDay", Day.Id.ToString(),
            $"{Employee.FullName}, {Day.Date:dd.MM.yyyy} (редактирование мастером)");
        TempData["Success"] = "Изменения сохранены.";
        return Redirect($"/Master/Day?employeeId={employeeId}&date={date:yyyy-MM-dd}");
    }

    private async Task<bool> LoadAsync(int employeeId, DateOnly date)
    {
        IsSectionMaster = _currentUser.IsInRole(AppRoles.SectionMaster);
        Employee = await _db.Employees
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.Id == employeeId);
        if (Employee is null) return false;

        // Мастер может работать только со своим участком.
        if (IsSectionMaster)
        {
            var me = await _currentUser.GetEmployeeAsync();
            if (me is null || me.DepartmentId != Employee.DepartmentId) return false;
        }

        Day = await _db.WorkDays
            .Include(w => w.TimeEntries).ThenInclude(t => t.Boat)
            .Include(w => w.Employee)
            .FirstOrDefaultAsync(w => w.EmployeeId == employeeId && w.Date == date);

        if (Day is null)
        {
            var shift = await _settings.GetShiftTemplateAsync();
            Day = new WorkDay { EmployeeId = employeeId, Date = date, StartTime = shift?.Start ?? ShiftDefaults.Start, EndTime = shift?.End ?? ShiftDefaults.End };
        }

        Input = new DayFormModel
        {
            Date = date,
            StartTime = Day.StartTime,
            EndTime = Day.EndTime,
            ReturnUrl = $"/Master/Day?employeeId={employeeId}&date={date:yyyy-MM-dd}",
            Entries = Day.TimeEntries.Select(e => new Pages.Employee.EntryInput
            {
                BoatId = e.BoatId,
                Hours = e.Hours,
                WorkDescription = e.WorkDescription,
                IsInhouse = e.IsInhouse,
            }).ToList(),
        };

        CanEdit = Day.Id == 0 || WorkflowService.CanMasterEdit(Day.Status)
            || (_currentUser.IsInRole(AppRoles.Admin) && Day.Status != TimeSheetStatus.Approved);
        Boats = await _db.Boats.Where(b => b.IsActive).OrderBy(b => b.Number).ToListAsync();
        WorkTypes = await _db.WorkTypes.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
        return true;
    }
}
