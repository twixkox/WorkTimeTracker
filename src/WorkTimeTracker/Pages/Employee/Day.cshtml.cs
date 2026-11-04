using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Employee;

public class DayModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly CurrentUserService _currentUser;
    private readonly SettingsService _settings;
    private readonly AuditService _audit;
    private readonly NotificationService _notifications;
    private readonly UserManager<ApplicationUser> _userManager;

    public DayModel(ApplicationDbContext db, CurrentUserService currentUser, SettingsService settings,
        AuditService audit, NotificationService notifications, UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _currentUser = currentUser;
        _settings = settings;
        _audit = audit;
        _notifications = notifications;
        _userManager = userManager;
    }

    [BindProperty]
    public DayFormModel Input { get; set; } = new();

    public Models.Employee? Employee { get; private set; }
    public WorkDay? Day { get; private set; }
    public List<Boat> Boats { get; private set; } = new();
    public List<WorkType> WorkTypes { get; private set; } = new();
    public bool CanEdit { get; private set; }
    public List<ValidationIssue> Issues { get; private set; } = new();
    public bool ShowWeekLink { get; private set; }

    public async Task<IActionResult> OnGet(DateOnly? date, string? returnUrl)
    {
        var target = date ?? DateOnly.FromDateTime(DateTime.Today);
        if (!await LoadAsync(target, returnUrl)) return RedirectToPage("/Employee/Index");
        return Page();
    }

    public Task<IActionResult> OnPostSaveAsync() => SaveAsync(submit: false);

    public Task<IActionResult> OnPostSubmitAsync() => SaveAsync(submit: true);

    private async Task<bool> LoadAsync(DateOnly date, string? returnUrl)
    {
        Employee = await _currentUser.GetEmployeeAsync();
        if (Employee is null) return false;

        var shift = await _settings.GetShiftTemplateAsync();
        Day = await _db.WorkDays
            .Include(w => w.TimeEntries)
            .FirstOrDefaultAsync(w => w.EmployeeId == Employee.Id && w.Date == date);

        Input = new DayFormModel
        {
            Date = date,
            StartTime = Day?.StartTime ?? shift?.Start ?? ShiftDefaults.Start,
            EndTime = Day?.EndTime ?? shift?.End ?? ShiftDefaults.End,
            ReturnUrl = string.IsNullOrWhiteSpace(returnUrl) ? "/Employee/Index" : returnUrl!,
            Entries = Day?.TimeEntries.Select(e => new EntryInput
            {
                BoatId = e.BoatId,
                Hours = e.Hours,
                WorkDescription = e.WorkDescription,
                IsInhouse = e.IsInhouse,
            }).ToList() ?? new List<EntryInput>(),
        };

        CanEdit = Day is null || WorkflowService.CanEmployeeEdit(Day.Status);
        ShowWeekLink = Employee.Department?.IsWeeklyView == true;
        Boats = await _db.Boats.Where(b => b.IsActive).OrderBy(b => b.Number).ToListAsync();
        WorkTypes = await _db.WorkTypes.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
        return true;
    }

    private async Task<IActionResult> SaveAsync(bool submit)
    {
        Employee = await _currentUser.GetEmployeeAsync();
        if (Employee is null)
            return RedirectToPage("/Employee/Index");

        var day = await _db.WorkDays
            .Include(w => w.TimeEntries)
            .FirstOrDefaultAsync(w => w.EmployeeId == Employee.Id && w.Date == Input.Date);

        if (day is not null && !WorkflowService.CanEmployeeEdit(day.Status))
        {
            TempData["Error"] = "Табель на утверждении или утверждён — редактирование недоступно.";
            return Redirect($"/Employee/Day?date={Input.Date:yyyy-MM-dd}");
        }

        day ??= new WorkDay { EmployeeId = Employee.Id, Date = Input.Date };
        day.StartTime = Input.StartTime;
        day.EndTime = Input.EndTime;

        var isTimeOnly = Employee.Department?.IsTimeOnly == true;
        var settings = await _settings.GetValidationSettingsAsync();
        var entries = isTimeOnly ? new List<EntryInput>() : Input.EffectiveEntries();
        var validation = TimeCalculationService.Validate(Input.StartTime, Input.EndTime, MapEntries(entries), settings, strict: submit && !isTimeOnly);

        foreach (var state in ModelState)
        {
            foreach (var error in state.Value.Errors)
            {
                validation.Add(ValidationSeverity.Error, "binding",
                    $"{state.Key}: {error.ErrorMessage}{(error.Exception is not null ? $" ({error.Exception.Message})" : "")}");
            }
        }

        if ((submit || isTimeOnly) && validation.HasErrors)
        {
            Issues = validation.Issues;
            await PrepareViewAsync(day);
            return Page();
        }
        if (entries.Count == 0 && submit && !isTimeOnly)
        {
            Issues = new List<ValidationIssue>
            {
                new(ValidationSeverity.Error, "empty", "Добавьте хотя бы одну запись о затратах времени."),
            };
            await PrepareViewAsync(day);
            return Page();
        }

        day.TimeEntries.Clear();
        foreach (var e in entries)
        {
            day.TimeEntries.Add(new TimeEntry
            {
                BoatId = e.BoatId,
                Hours = e.Hours ?? 0,
                WorkDescription = e.WorkDescription ?? string.Empty,
                IsInhouse = e.IsInhouse,
            });
        }

        var isNew = day.Id == 0;
        if (isNew) _db.WorkDays.Add(day);
        await _db.SaveChangesAsync();

        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName,
            isNew ? "Created" : "Updated", "WorkDay", day.Id.ToString(), $"{Employee.FullName}, {day.Date:dd.MM.yyyy}");

        if (submit && !isTimeOnly)
        {
            if (WorkflowService.TrySubmit(day))
            {
                await _db.SaveChangesAsync();
                await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Submitted", "WorkDay", day.Id.ToString(), day.Date.ToString("dd.MM.yyyy"));
                await NotifyMastersAsync(day);
                TempData["Success"] = $"Табель за {day.Date:dd.MM.yyyy} отправлен на утверждение.";
            }
        }
        else
        {
            TempData["Success"] = isTimeOnly
                ? $"Время за {day.Date:dd.MM.yyyy} сохранено."
                : $"Черновик за {day.Date:dd.MM.yyyy} сохранён.";
        }

        if (!string.IsNullOrEmpty(Input.ReturnUrl) && Url.IsLocalUrl(Input.ReturnUrl))
            return Redirect(Input.ReturnUrl);
        return RedirectToPage("/Employee/Index");
    }

    private async Task PrepareViewAsync(WorkDay day)
    {
        Day = day;
        CanEdit = true;
        ShowWeekLink = Employee!.Department?.IsWeeklyView == true;
        Boats = await _db.Boats.Where(b => b.IsActive).OrderBy(b => b.Number).ToListAsync();
        WorkTypes = await _db.WorkTypes.Where(w => w.IsActive).OrderBy(w => w.Name).ToListAsync();
    }

    private async Task NotifyMastersAsync(WorkDay day)
    {
        var masters = await _db.Employees
            .Where(e => e.DepartmentId == Employee!.DepartmentId && e.UserId != null && e.IsActive)
            .ToListAsync();
        foreach (var master in masters)
        {
            var roles = await _userManager.GetRolesAsync((await _userManager.FindByIdAsync(master.UserId!))!);
            if (!roles.Contains(AppRoles.SectionMaster)) continue;
            await _notifications.NotifyAsync(master.UserId!, "Табель на утверждении",
                $"{Employee!.FullName} отправил табель за {day.Date:dd.MM.yyyy}.",
                $"/Master/Day?employeeId={Employee.Id}&date={day.Date:yyyy-MM-dd}");
        }
    }

    private static List<TimeEntry> MapEntries(List<EntryInput> entries)
        => entries.Select(e => new TimeEntry
        {
            BoatId = e.IsInhouse ? null : e.BoatId,
            Hours = e.Hours ?? 0,
            WorkDescription = e.WorkDescription ?? string.Empty,
            IsInhouse = e.IsInhouse,
        }).ToList();
}
