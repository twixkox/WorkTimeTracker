using System.ComponentModel.DataAnnotations;
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
    private readonly NotificationService _notifications;
    private readonly AuditService _audit;

    public EditModel(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        CurrentUserService currentUser, NotificationService notifications, AuditService audit)
    {
        _db = db;
        _userManager = userManager;
        _currentUser = currentUser;
        _notifications = notifications;
        _audit = audit;
    }

    public EngineeringTask? Item { get; private set; }
    public SelectList Engineers { get; private set; } = default!;
    private bool CanEdit => _currentUser.IsInRole(AppRoles.ChiefEngineer) || _currentUser.IsInRole(AppRoles.Admin);

    [BindProperty]
    [StringLength(1000, ErrorMessage = "Название задачи не может быть длиннее 1000 символов.")]
    public string Title { get; set; } = string.Empty;

    [BindProperty]
    public string? AssigneeId { get; set; }

    [BindProperty]
    public DateOnly? StartedOn { get; set; }

    [BindProperty]
    public DateOnly? PlannedDueOn { get; set; }

    [BindProperty]
    public DateOnly? CompletedOn { get; set; }

    [BindProperty]
    public DateOnly? Deadline { get; set; }

    [BindProperty]
    [StringLength(100, ErrorMessage = "Номер корпуса не может быть длиннее 100 символов.")]
    public string? HullNumber { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        if (!CanEdit) return Forbid();
        Item = await _db.EngineeringTasks.AsNoTracking().FirstOrDefaultAsync(t => t.Id == id);
        if (Item is null) return NotFound();

        Title = Item.Title;
        AssigneeId = Item.AssigneeId;
        StartedOn = Item.StartedOn;
        PlannedDueOn = Item.PlannedDueOn;
        CompletedOn = Item.CompletedOn;
        Deadline = Item.Deadline;
        HullNumber = Item.HullNumber;
        await LoadEngineersAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!CanEdit) return Forbid();
        Item = await _db.EngineeringTasks.Include(t => t.Assignee).FirstOrDefaultAsync(t => t.Id == id);
        if (Item is null) return NotFound();

        var engineers = await GetActiveEngineersAsync();
        SetEngineers(engineers);
        ApplicationUser? assignee = null;
        if (!string.IsNullOrWhiteSpace(AssigneeId))
        {
            assignee = engineers.FirstOrDefault(u => u.Id == AssigneeId);
            if (assignee is null)
                ModelState.AddModelError(nameof(AssigneeId), "Выберите активного пользователя с ролью «Инженер».");
        }
        if (string.IsNullOrWhiteSpace(Title))
            ModelState.AddModelError(nameof(Title), "Укажите название задачи.");
        if (StartedOn is { } startedOn && CompletedOn is { } completedOn && completedOn < startedOn)
            ModelState.AddModelError(nameof(CompletedOn), "Дата завершения не может быть раньше даты начала.");
        if (!ModelState.IsValid) return Page();

        var previousAssigneeId = Item.AssigneeId;
        var previousAssignee = Item.Assignee is null
            ? "не назначен"
            : UserName(Item.Assignee);
        Item.Title = Title.Trim();
        Item.AssigneeId = assignee?.Id;
        Item.StartedOn = StartedOn;
        Item.PlannedDueOn = PlannedDueOn;
        Item.CompletedOn = CompletedOn;
        Item.Deadline = Deadline;
        Item.HullNumber = string.IsNullOrWhiteSpace(HullNumber) ? null : HullNumber.Trim();
        await _db.SaveChangesAsync();

        var newAssignee = assignee is null ? "не назначен" : UserName(assignee);
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Updated", "EngineeringTask",
            Item.Id.ToString(), $"{Item.Title}; исполнитель: {previousAssignee} → {newAssignee}");
        if (assignee is not null && previousAssigneeId != assignee.Id)
        {
            var deadline = Item.Deadline is { } date ? $" Дедлайн: {date:dd.MM.yyyy}." : string.Empty;
            await _notifications.NotifyAsync(assignee.Id, "Вам назначена задача КБ",
                $"{Item.Title}.{deadline}", "/Engineering/Index");
        }

        TempData["Success"] = "Задача сохранена.";
        return RedirectToPage("./Index");
    }

    private async Task LoadEngineersAsync() => SetEngineers(await GetActiveEngineersAsync());

    private async Task<List<ApplicationUser>> GetActiveEngineersAsync()
        => (await _userManager.GetUsersInRoleAsync(AppRoles.Engineer))
            .Where(u => u.IsActive)
            .OrderBy(u => u.DisplayName)
            .ThenBy(u => u.Email)
            .ToList();

    private void SetEngineers(List<ApplicationUser> engineers)
        => Engineers = new SelectList(engineers.Select(u => new { u.Id, Name = UserName(u) }), "Id", "Name", AssigneeId);

    private static string UserName(ApplicationUser user)
        => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? user.Id : user.DisplayName;
}
