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

    public async Task<IActionResult> OnPostAsync(int id)
    {
        if (!CanEdit) return Forbid();
        Item = await _db.EngineeringTasks.Include(t => t.Assignee).FirstOrDefaultAsync(t => t.Id == id);
        if (Item is null) return NotFound();

        var engineers = await GetActiveEngineersAsync();
        SetEngineers(engineers);
        var boats = await GetActiveBoatsAsync();
        SetBoats(boats);

        ApplicationUser? assignee = null;
        if (!string.IsNullOrWhiteSpace(AssigneeId))
        {
            assignee = engineers.FirstOrDefault(u => u.Id == AssigneeId);
            if (assignee is null)
                ModelState.AddModelError(nameof(AssigneeId), "Выберите активного пользователя с ролью «Инженер».");
        }
        if (BoatId is { } boatId && boats.All(b => b.Id != boatId))
            ModelState.AddModelError(nameof(BoatId), "Выберите активный корпус из справочника.");
        if (string.IsNullOrWhiteSpace(Title))
            ModelState.AddModelError(nameof(Title), "Укажите название задачи.");
        if (!Enum.IsDefined(Priority))
            ModelState.AddModelError(nameof(Priority), "Выберите корректный приоритет.");
        if (!Enum.IsDefined(Status))
            ModelState.AddModelError(nameof(Status), "Выберите корректный статус.");

        var previousAssigneeId = Item.AssigneeId;
        var previousStatus = Item.Status;
        var previousPriority = Item.Priority;
        var previousDeadline = Item.Deadline;
        var deadlineChanged = Deadline != previousDeadline;
        if (deadlineChanged && previousDeadline is not null && string.IsNullOrWhiteSpace(DeadlineChangeComment))
            ModelState.AddModelError(nameof(DeadlineChangeComment), "Укажите причину изменения дедлайна.");

        var removingAssignee = assignee is null && previousAssigneeId is not null;
        var nextStatus = Status;
        if (removingAssignee)
        {
            nextStatus = EngineeringTaskStatus.New;
        }
        else
        {
            if (!IsValidTransition(previousStatus, Status))
                ModelState.AddModelError(nameof(Status), "Статус можно изменить только на один шаг вперёд или назад.");

            if (assignee is null)
                nextStatus = EngineeringTaskStatus.New;
            else if (nextStatus == EngineeringTaskStatus.New)
                nextStatus = EngineeringTaskStatus.Assigned;

            if (nextStatus != Status && !IsValidTransition(previousStatus, nextStatus))
                ModelState.AddModelError(nameof(Status), "Недопустимый переход статуса.");
        }
        if (nextStatus == EngineeringTaskStatus.Done && assignee is null)
            ModelState.AddModelError(nameof(Status), "Нельзя завершить задачу без исполнителя.");
        if (!ModelState.IsValid) return Page();

        var previousAssignee = Item.Assignee is null
            ? "не назначен"
            : UserName(Item.Assignee);
        Item.Title = Title.Trim();
        Item.Priority = Priority;
        Item.Status = nextStatus;
        Item.AssigneeId = assignee?.Id;
        Item.BoatId = BoatId;
        Item.PlannedDueOn = PlannedDueOn;
        Item.Deadline = Deadline;
        ApplyStatusDates(Item, previousStatus, DateOnly.FromDateTime(DateTime.Today));
        if (deadlineChanged)
        {
            Item.DeadlineReminderSentOn = null;
            Item.OverdueReminderSentOn = null;
        }
        await _db.SaveChangesAsync();

        var newAssignee = assignee is null ? "не назначен" : UserName(assignee);
        await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "Updated", "EngineeringTask",
            Item.Id.ToString(), $"{Item.Title}; исполнитель: {previousAssignee} → {newAssignee}");
        if (deadlineChanged)
        {
            var comment = string.IsNullOrWhiteSpace(DeadlineChangeComment) ? "не указан" : DeadlineChangeComment.Trim();
            await _audit.LogAsync(_currentUser.UserId ?? "", _currentUser.UserName, "DeadlineChanged", "EngineeringTask",
                Item.Id.ToString(), $"Дедлайн: {FormatDate(previousDeadline)} → {FormatDate(Item.Deadline)}; комментарий: {comment}");
            if (assignee is not null)
            {
                await _notifications.NotifyAsync(assignee.Id, "Изменён дедлайн задачи КБ",
                    $"{Item.Title}. Дедлайн: {FormatDate(previousDeadline)} → {FormatDate(Item.Deadline)}. Комментарий: {comment}",
                    "/Engineering/Index");
            }
        }
        if (previousPriority != Item.Priority && assignee is not null)
        {
            await _notifications.NotifyAsync(assignee.Id, "Изменён приоритет задачи КБ",
                $"{Item.Title}. Приоритет: {PriorityName(previousPriority)} → {PriorityName(Item.Priority)}.",
                "/Engineering/Index");
        }
        if (assignee is not null && previousAssigneeId != assignee.Id)
        {
            var deadline = Item.Deadline is { } date ? $" Дедлайн: {date:dd.MM.yyyy}." : string.Empty;
            await _notifications.NotifyAsync(assignee.Id, "Вам назначена задача КБ",
                $"{Item.Title}.{deadline}", "/Engineering/Index");
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

    private static bool IsValidTransition(EngineeringTaskStatus current, EngineeringTaskStatus next)
        => next == current || (int)next == (int)current + 1 || (int)next == (int)current - 1;

    private static void ApplyStatusDates(EngineeringTask item, EngineeringTaskStatus previousStatus, DateOnly today)
    {
        if (item.Status == EngineeringTaskStatus.InProgress && previousStatus != EngineeringTaskStatus.InProgress)
            item.StartedOn ??= today;
        if (item.Status == EngineeringTaskStatus.Done && previousStatus != EngineeringTaskStatus.Done)
        {
            item.StartedOn ??= today;
            item.CompletedOn = today;
        }
        if (previousStatus == EngineeringTaskStatus.Done && item.Status != EngineeringTaskStatus.Done)
            item.CompletedOn = null;
        if (previousStatus == EngineeringTaskStatus.InProgress && item.Status < EngineeringTaskStatus.InProgress)
        {
            item.StartedOn = null;
            item.CompletedOn = null;
        }
        if (item.Status == EngineeringTaskStatus.New)
        {
            item.StartedOn = null;
            item.CompletedOn = null;
        }
    }

    private static string FormatDate(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "не задан";

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
