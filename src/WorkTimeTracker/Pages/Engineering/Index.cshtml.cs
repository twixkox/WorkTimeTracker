using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
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
    private readonly EngineeringTaskService _tasks;

    public IndexModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        CurrentUserService currentUser, EngineeringTaskService tasks)
    {
        _db = db;
        _userManager = userManager;
        _currentUser = currentUser;
        _tasks = tasks;
    }

    public List<EngineeringTask> Tasks { get; private set; } = new();
    public SelectList Boats { get; private set; } = default!;
    public string Filter { get; private set; } = "active";
    public EngineeringTaskPriority? PriorityFilter { get; private set; }
    public string? Search { get; private set; }
    public bool CanCreate => _currentUser.IsInRole(AppRoles.ProductionHead)
        || _currentUser.IsInRole(AppRoles.ChiefEngineer)
        || _currentUser.IsInRole(AppRoles.Admin);
    public bool CanEdit => _currentUser.IsInRole(AppRoles.ChiefEngineer) || _currentUser.IsInRole(AppRoles.Admin);

    [BindProperty]
    [StringLength(1000, ErrorMessage = "Название задачи не может быть длиннее 1000 символов.")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public EngineeringTaskPriority Priority { get; set; } = EngineeringTaskPriority.Normal;

    [BindProperty]
    public int? BoatId { get; set; }

    [BindProperty]
    public DateOnly? Deadline { get; set; }

    public async Task OnGetAsync(string? filter, EngineeringTaskPriority? priority, string? search)
    {
        SetFilters(filter, priority, search);
        if (CanCreate) await LoadBoatsAsync();
        await LoadTasksAsync();
    }

    public async Task<IActionResult> OnPostCreateAsync(string? filter, EngineeringTaskPriority? priorityFilter,
        string? search, CancellationToken cancellationToken = default)
    {
        if (!CanCreate) return Forbid();
        SetFilters(filter, priorityFilter, search);
        if (string.IsNullOrWhiteSpace(Title))
            ModelState.AddModelError(nameof(Title), "Укажите название задачи.");
        if (!Enum.IsDefined(Priority))
            ModelState.AddModelError(nameof(Priority), "Выберите приоритет задачи.");
        if (BoatId is { } boatId && !await _db.Boats.AnyAsync(b => b.Id == boatId && b.IsActive))
            ModelState.AddModelError(nameof(BoatId), "Выберите активный катер из справочника.");
        if (!ModelState.IsValid)
        {
            await LoadBoatsAsync();
            await LoadTasksAsync();
            return Page();
        }

        var userId = _currentUser.UserId;
        if (userId is null) return Forbid();
        var creator = await _userManager.FindByIdAsync(userId);
        var createdByName = !string.IsNullOrWhiteSpace(creator?.DisplayName)
            ? creator.DisplayName
            : _currentUser.UserName;
        var roles = _currentUser.User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()
            ?? Array.Empty<string>();
        var actor = new EngineeringTaskActor(userId, createdByName, roles);
        var result = await _tasks.CreateAsync(actor, Title.Trim(), Priority, BoatId,
            null, Deadline, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(result.Field ?? string.Empty,
                result.Error ?? "Не удалось создать задание.");
            await LoadBoatsAsync();
            await LoadTasksAsync();
            return Page();
        }

        TempData["Success"] = "Задание создано.";
        return RedirectToPage(new { filter = Filter, priority = PriorityFilter, search = Search });
    }

    public bool IsOverdue(EngineeringTask task)
        => task.Status != EngineeringTaskStatus.Done
            && task.Deadline is { } deadline
            && deadline < DateOnly.FromDateTime(DateTime.Today);

    public bool IsDueSoon(EngineeringTask task)
    {
        var today = DateOnly.FromDateTime(DateTime.Today);
        return task.Status != EngineeringTaskStatus.Done
            && task.Deadline is { } deadline
            && deadline >= today
            && deadline <= today.AddDays(2);
    }

    public string Status(EngineeringTask task) => task.Status switch
    {
        EngineeringTaskStatus.Assigned => "Назначена",
        EngineeringTaskStatus.InProgress => "В работе",
        EngineeringTaskStatus.Done => "Готова",
        _ => "Новая",
    };

    public string StatusClass(EngineeringTask task) => task.Status switch
    {
        EngineeringTaskStatus.Assigned => "text-bg-warning",
        EngineeringTaskStatus.InProgress => "text-bg-primary",
        EngineeringTaskStatus.Done => "text-bg-success",
        _ => "text-bg-secondary",
    };

    public string PriorityText(EngineeringTask task) => task.Priority switch
    {
        EngineeringTaskPriority.Low => "Низкий",
        EngineeringTaskPriority.High => "Высокий",
        EngineeringTaskPriority.Urgent => "Срочный",
        _ => "Обычный",
    };

    public string PriorityClass(EngineeringTask task) => task.Priority switch
    {
        EngineeringTaskPriority.Low => "text-bg-secondary",
        EngineeringTaskPriority.High => "text-bg-warning",
        EngineeringTaskPriority.Urgent => "text-bg-danger",
        _ => "text-bg-info",
    };

    private void SetFilters(string? filter, EngineeringTaskPriority? priority, string? search)
    {
        Filter = filter is "completed" or "all" or "overdue" ? filter : "active";
        PriorityFilter = priority;
        Search = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
    }

    private async Task LoadBoatsAsync()
    {
        var boats = await _db.Boats.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Number)
            .ThenBy(b => b.Name)
            .ToListAsync();
        Boats = new SelectList(boats.Select(b => new
        {
            b.Id,
            Text = string.IsNullOrWhiteSpace(b.Name) ? b.Number : $"{b.Number} — {b.Name}",
        }), "Id", "Text", BoatId);
    }

    private async Task LoadTasksAsync()
    {
        var query = _db.EngineeringTasks
            .Include(t => t.Assignee)
            .Include(t => t.Boat)
            .AsNoTracking()
            .AsQueryable();
        var today = DateOnly.FromDateTime(DateTime.Today);
        query = Filter switch
        {
            "completed" => query.Where(t => t.Status == EngineeringTaskStatus.Done),
            "overdue" => query.Where(t => t.Status != EngineeringTaskStatus.Done && t.Deadline < today),
            "all" => query,
            _ => query.Where(t => t.Status != EngineeringTaskStatus.Done),
        };
        if (PriorityFilter is { } priority) query = query.Where(t => t.Priority == priority);
        if (Search is not null)
        {
            var search = Search.ToLower();
            query = query.Where(t => t.Title.ToLower().Contains(search)
                || t.CreatedByName.ToLower().Contains(search)
                || (t.HullNumber != null && t.HullNumber.ToLower().Contains(search))
                || (t.Boat != null && (t.Boat.Number.ToLower().Contains(search)
                    || (t.Boat.Name != null && t.Boat.Name.ToLower().Contains(search))))
                || (t.Assignee != null && (t.Assignee.DisplayName.ToLower().Contains(search)
                    || (t.Assignee.Email != null && t.Assignee.Email.ToLower().Contains(search)))));
        }

        Tasks = await query
            .OrderByDescending(t => t.Priority)
            .ThenBy(t => t.Deadline == null)
            .ThenBy(t => t.Deadline)
            .ThenByDescending(t => t.CreatedAt)
            .ToListAsync();
    }
}
