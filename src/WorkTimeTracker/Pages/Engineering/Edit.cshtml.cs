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

public class EditModel : PageModel
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly CurrentUserService _currentUser;
    private readonly EngineeringTaskService _tasks;

    public EditModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        CurrentUserService currentUser, EngineeringTaskService tasks)
    {
        _db = db;
        _userManager = userManager;
        _currentUser = currentUser;
        _tasks = tasks;
    }

    public EngineeringTask? Item { get; private set; }
    public SelectList Engineers { get; private set; } = default!;
    public SelectList Boats { get; private set; } = default!;
    private bool CanEdit => _currentUser.IsInRole(AppRoles.ChiefEngineer) || _currentUser.IsInRole(AppRoles.Admin);

    [BindProperty]
    [StringLength(1000, ErrorMessage = "Название задачи не может быть длиннее 1000 символов.")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public EngineeringTaskPriority Priority { get; set; }

    [BindProperty]
    public EngineeringTaskStatus Status { get; set; }

    [BindProperty]
    public string? AssigneeId { get; set; }

    [BindProperty]
    public int? BoatId { get; set; }

    [BindProperty]
    public DateOnly? PlannedDueOn { get; set; }

    [BindProperty]
    public DateOnly? Deadline { get; set; }

    [BindProperty]
    [StringLength(1000, ErrorMessage = "Комментарий к переносу дедлайна не может быть длиннее 1000 символов.")]
    public string? DeadlineChangeComment { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!CanEdit) return Forbid();
        Item = await _db.EngineeringTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (Item is null) return NotFound();

        Title = Item.Title;
        Priority = Item.Priority;
        Status = Item.Status;
        AssigneeId = Item.AssigneeId;
        BoatId = Item.BoatId;
        PlannedDueOn = Item.PlannedDueOn;
        Deadline = Item.Deadline;
        await LoadEngineersAsync();
        await LoadBoatsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id, CancellationToken cancellationToken = default)
    {
        if (!CanEdit) return Forbid();
        Item = await _db.EngineeringTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (Item is null) return NotFound();

        await LoadEngineersAsync();
        await LoadBoatsAsync();
        if (!ModelState.IsValid) return Page();

        var userId = _currentUser.UserId;
        if (userId is null) return Forbid();
        var roles = _currentUser.User?.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray()
            ?? Array.Empty<string>();
        var actor = new EngineeringTaskActor(userId, _currentUser.UserName, roles);
        var input = new EngineeringTaskUpdateInput
        {
            Title = Title,
            Priority = Priority,
            Status = Status,
            AssigneeId = AssigneeId,
            BoatId = BoatId,
            PlannedDueOn = PlannedDueOn,
            Deadline = Deadline,
            DeadlineChangeComment = DeadlineChangeComment,
        };
        var result = await _tasks.UpdateAsync(actor, id, input, cancellationToken);
        if (!result.Success)
        {
            ModelState.AddModelError(result.Field ?? string.Empty,
                result.Error ?? "Не удалось сохранить задачу.");
            return Page();
        }

        TempData["Success"] = "Задача сохранена.";
        return RedirectToPage("./Index");
    }

    public static string PriorityName(EngineeringTaskPriority priority) => priority switch
    {
        EngineeringTaskPriority.Low => "Низкий",
        EngineeringTaskPriority.Normal => "Обычный",
        EngineeringTaskPriority.High => "Высокий",
        EngineeringTaskPriority.Urgent => "Срочный",
        _ => priority.ToString()
    };

    public static string StatusName(EngineeringTaskStatus status) => status switch
    {
        EngineeringTaskStatus.New => "Новая",
        EngineeringTaskStatus.Assigned => "Назначена",
        EngineeringTaskStatus.InProgress => "В работе",
        EngineeringTaskStatus.Done => "Готова",
        _ => status.ToString()
    };

    private async Task LoadEngineersAsync() => SetEngineers(await GetActiveEngineersAsync());

    private async Task LoadBoatsAsync() => SetBoats(await GetActiveBoatsAsync());

    private async Task<List<ApplicationUser>> GetActiveEngineersAsync()
        => (await _userManager.GetUsersInRoleAsync(AppRoles.Engineer))
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .ToList();

    private async Task<List<Boat>> GetActiveBoatsAsync()
        => await _db.Boats.AsNoTracking()
            .Where(b => b.IsActive)
            .OrderBy(b => b.Number)
            .ThenBy(b => b.Name)
            .ToListAsync();

    private void SetEngineers(List<ApplicationUser> engineers)
        => Engineers = new SelectList(engineers.Select(u => new { u.Id, Name = UserName(u) }), "Id", "Name", AssigneeId);

    private void SetBoats(List<Boat> boats)
        => Boats = new SelectList(boats.Select(b => new
        {
            b.Id,
            Name = string.IsNullOrWhiteSpace(b.Name) ? b.Number : $"{b.Number} · {b.Name}"
        }), "Id", "Name", BoatId);

    private static string UserName(ApplicationUser user)
        => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? user.Id : user.DisplayName;
}
