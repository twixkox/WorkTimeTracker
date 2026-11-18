using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Engineering;

public class IndexModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notifications;
    private readonly AuditService _audit;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        CurrentUserService currentUser, NotificationService notifications, AuditService audit)
    {
        _db = db;
        _userManager = userManager;
        _currentUser = currentUser;
        _notifications = notifications;
        _audit = audit;
    }

    public List<EngineeringTask> Tasks { get; private set; } = new();
    public string Filter { get; private set; } = "active";
    public string? Search { get; private set; }
    public bool CanCreate => _currentUser.IsInRole(AppRoles.ProductionHead) || _currentUser.IsInRole(AppRoles.Admin);
    public bool CanEdit => _currentUser.IsInRole(AppRoles.ChiefEngineer) || _currentUser.IsInRole(AppRoles.Admin);

    [BindProperty]
    [StringLength(1000, ErrorMessage = "Название задачи не может быть длиннее 1000 символов.")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    [StringLength(100, ErrorMessage = "Номер корпуса не может быть длиннее 100 символов.")]
    public string? HullNumber { get; set; }

    public async Task OnGetAsync(string? filter, string? search)
    {
        SetFilters(filter, search);
        await LoadTasksAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? filter, string? search)
    {
        if (!CanCreate) return Forbid();
        SetFilters(filter, search);
        if (string.IsNullOrWhiteSpace(Title))
            ModelState.AddModelError(nameof(Title), "Укажите название задачи.");
        if (!ModelState.IsValid)
        {
            await LoadTasksAsync();
            return Page();
        }

        var userId = _currentUser.UserId;
        if (userId is null) return Forbid();
        var creator = await _userManager.FindByIdAsync(userId);
        var createdByName = !string.IsNullOrWhiteSpace(creator?.DisplayName)
            ? creator.DisplayName
            : _currentUser.UserName;

        var task = new EngineeringTask
        {
            CreatedAt = DateTime.Now,
            CreatedByUserId = userId,
            CreatedByName = createdByName,
            Title = Title.Trim(),
            HullNumber = string.IsNullOrWhiteSpace(HullNumber) ? null : HullNumber.Trim(),
        };
        _db.EngineeringTasks.Add(task);
        await _db.SaveChangesAsync();
        await _audit.LogAsync(userId, _currentUser.UserName, "Created", "EngineeringTask", task.Id.ToString(), task.Title);

        var chiefEngineers = await _userManager.GetUsersInRoleAsync(AppRoles.ChiefEngineer);
        foreach (var user in chiefEngineers.Where(u => u.IsActive))
        {
            await _notifications.NotifyAsync(user.Id, "Новое задание КБ",
                $"{createdByName}: {task.Title}", $"/Engineering/Edit/{task.Id}");
        }

        TempData["Success"] = "Задание создано.";
        return RedirectToPage(new { filter = Filter, search = Search });
    }

    public bool IsOverdue(EngineeringTask task)
        => task.CompletedOn is null && task.Deadline < DateOnly.FromDateTime(DateTime.Today);

    public string Status(EngineeringTask task)
    {
        if (task.CompletedOn is not null) return "Завершено";
        if (task.StartedOn is not null) return "В работе";
        if (task.AssigneeId is not null) return "Назначено";
        return "Новая";
    }

    public string StatusClass(EngineeringTask task)
    {
        if (task.CompletedOn is not null) return "text-bg-success";
        if (IsOverdue(task)) return "text-bg-danger";
        if (task.StartedOn is not null) return "text-bg-primary";
        if (task.AssigneeId is not null) return "text-bg-warning";
        return "text-bg-secondary";
    }

    private void SetFilters(string? filter, string? search)
    {
        Filter = filter is "completed" or "all" ? filter : "active";
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
    }

    private async Task LoadTasksAsync()
    {
        var query = _db.EngineeringTasks.Include(t => t.Assignee).AsNoTracking().AsQueryable();
        if (Filter == "active") query = query.Where(t => t.CompletedOn == null);
        if (Filter == "completed") query = query.Where(t => t.CompletedOn != null);
        if (Search is not null)
        {
            var search = Search.ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(search)
                || t.CreatedByName.ToLower().Contains(search)
                || (t.HullNumber != null && t.HullNumber.ToLower().Contains(search))
                || (t.Assignee != null && (t.Assignee.DisplayName.ToLower().Contains(search)
                    || (t.Assignee.Email != null && t.Assignee.Email.ToLower().Contains(search)))));
        }

        Tasks = await query.OrderByDescending(t => t.CreatedAt).ToListAsync();
    }
}
