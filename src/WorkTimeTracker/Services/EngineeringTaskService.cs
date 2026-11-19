using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public sealed record EngineeringTaskActor(
    string UserId,
    string UserName,
    IReadOnlyCollection<string> Roles)
{
    public bool IsInRole(string role) => Roles.Contains(role, StringComparer.Ordinal);
}

public class EngineeringTaskUpdateInput
{
    public string Title { get; set; } = string.Empty;
    public EngineeringTaskPriority Priority { get; set; }
    public EngineeringTaskStatus Status { get; set; }
    public string? AssigneeId { get; set; }
    public int? BoatId { get; set; }
    public DateOnly? PlannedDueOn { get; set; }
    public DateOnly? Deadline { get; set; }
    public string? DeadlineChangeComment { get; set; }
}

public class EngineeringTaskOperationResult
{
    public bool Success { get; set; }
    public string? Error { get; set; }
    public string? Field { get; set; }
    public EngineeringTask? Task { get; set; }
    public EngineeringTaskAttachment? Attachment { get; set; }
}

public class EngineeringTaskService
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly NotificationService _notifications;
    private readonly AuditService _audit;

    public EngineeringTaskService(ApplicationDbContext db, UserManager<ApplicationUser> userManager,
        NotificationService notifications, AuditService audit)
    {
        _db = db;
        _userManager = userManager;
        _notifications = notifications;
        _audit = audit;
    }

    public async Task<EngineeringTaskOperationResult> CreateAsync(EngineeringTaskActor actor,
        string title, EngineeringTaskPriority priority, int? boatId, string? hullNumber,
        DateOnly? deadline, CancellationToken cancellationToken = default)
    {
        if (!CanCreate(actor)) return Failure("Недостаточно прав для создания задачи.");

        var validation = await ValidateTaskInputAsync(title, priority, boatId, hullNumber, cancellationToken);
        if (validation is not null) return validation;

        var task = new EngineeringTask
        {
            CreatedAt = DateTime.Now,
            CreatedByUserId = actor.UserId,
            CreatedByName = actor.UserName,
            Title = title.Trim(),
            Priority = priority,
            Status = EngineeringTaskStatus.New,
            BoatId = boatId,
            HullNumber = string.IsNullOrWhiteSpace(hullNumber) ? null : hullNumber.Trim(),
            Deadline = deadline,
        };
        _db.EngineeringTasks.Add(task);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(actor.UserId, actor.UserName, "Created", "EngineeringTask",
            task.Id.ToString(), task.Title);

        var chiefEngineers = await _userManager.GetUsersInRoleAsync(AppRoles.ChiefEngineer);
        foreach (var user in chiefEngineers.Where(u => u.IsActive))
        {
            await _notifications.NotifyAsync(user.Id, "Новое задание КБ",
                $"{task.CreatedByName}: {task.Title}", $"/Engineering/Edit/{task.Id}");
        }

        return Success(task);
    }

    public async Task<EngineeringTaskOperationResult> UpdateAsync(EngineeringTaskActor actor, int id,
        EngineeringTaskUpdateInput input, CancellationToken cancellationToken = default)
    {
        if (!CanEdit(actor)) return Failure("Недостаточно прав для редактирования задачи.");

        var task = await _db.EngineeringTasks.Include(t => t.Assignee)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null) return Failure("Задача не найдена.");

        var validation = await ValidateTaskInputAsync(input.Title, input.Priority, input.BoatId,
            task.HullNumber, cancellationToken);
        if (validation is not null) return validation;
        if (!Enum.IsDefined(input.Status))
            return Failure("Выберите корректный статус.", nameof(input.Status));
        if (input.DeadlineChangeComment?.Length > 1000)
            return Failure("Комментарий к переносу дедлайна не может быть длиннее 1000 символов.",
                nameof(input.DeadlineChangeComment));

        ApplicationUser? assignee = null;
        if (!string.IsNullOrWhiteSpace(input.AssigneeId))
        {
            assignee = await GetActiveEngineerAsync(input.AssigneeId);
            if (assignee is null)
                return Failure("Выберите активного пользователя с ролью «Инженер».", nameof(input.AssigneeId));
        }

        var previousAssigneeId = task.AssigneeId;
        var previousStatus = task.Status;
        var previousPriority = task.Priority;
        var previousDeadline = task.Deadline;
        var deadlineChanged = input.Deadline != previousDeadline;
        if (deadlineChanged && previousDeadline is not null
            && string.IsNullOrWhiteSpace(input.DeadlineChangeComment))
        {
            return Failure("Укажите причину изменения дедлайна.", nameof(input.DeadlineChangeComment));
        }

        var removingAssignee = assignee is null && previousAssigneeId is not null;
        var nextStatus = input.Status;
        if (removingAssignee)
        {
            nextStatus = EngineeringTaskStatus.New;
        }
        else
        {
            if (!IsValidTransition(previousStatus, input.Status))
                return Failure("Статус можно изменить только на один шаг вперёд или назад.", nameof(input.Status));

            if (assignee is null)
                nextStatus = EngineeringTaskStatus.New;
            else if (nextStatus == EngineeringTaskStatus.New)
                nextStatus = EngineeringTaskStatus.Assigned;

            if (nextStatus != input.Status && !IsValidTransition(previousStatus, nextStatus))
                return Failure("Недопустимый переход статуса.", nameof(input.Status));
        }
        if (nextStatus == EngineeringTaskStatus.Done && assignee is null)
            return Failure("Нельзя завершить задачу без исполнителя.", nameof(input.Status));

        var previousAssignee = task.Assignee is null ? "не назначен" : UserName(task.Assignee);
        task.Title = input.Title.Trim();
        task.Priority = input.Priority;
        task.Status = nextStatus;
        task.AssigneeId = assignee?.Id;
        task.BoatId = input.BoatId;
        task.PlannedDueOn = input.PlannedDueOn;
        task.Deadline = input.Deadline;
        ApplyStatusDates(task, previousStatus, DateTime.Now);
        if (deadlineChanged)
        {
            task.DeadlineReminderSentOn = null;
            task.OverdueReminderSentOn = null;
        }
        await _db.SaveChangesAsync(cancellationToken);

        var newAssignee = assignee is null ? "не назначен" : UserName(assignee);
        await _audit.LogAsync(actor.UserId, actor.UserName, "Updated", "EngineeringTask",
            task.Id.ToString(), $"{task.Title}; исполнитель: {previousAssignee} → {newAssignee}");
        if (deadlineChanged)
        {
            var comment = string.IsNullOrWhiteSpace(input.DeadlineChangeComment)
                ? "не указан"
                : input.DeadlineChangeComment.Trim();
            await _audit.LogAsync(actor.UserId, actor.UserName, "DeadlineChanged", "EngineeringTask",
                task.Id.ToString(), $"Дедлайн: {FormatDate(previousDeadline)} → {FormatDate(task.Deadline)}; комментарий: {comment}");
            if (assignee is not null)
            {
                await _notifications.NotifyAsync(assignee.Id, "Изменён дедлайн задачи КБ",
                    $"{task.Title}. Дедлайн: {FormatDate(previousDeadline)} → {FormatDate(task.Deadline)}. Комментарий: {comment}",
                    "/Engineering/Index");
            }
        }
        if (previousPriority != task.Priority && assignee is not null)
        {
            await _notifications.NotifyAsync(assignee.Id, "Изменён приоритет задачи КБ",
                $"{task.Title}. Приоритет: {PriorityName(previousPriority)} → {PriorityName(task.Priority)}.",
                "/Engineering/Index");
        }
        if (assignee is not null && previousAssigneeId != assignee.Id)
        {
            var deadline = task.Deadline is { } date ? $" Дедлайн: {date:dd.MM.yyyy}." : string.Empty;
            await _notifications.NotifyAsync(assignee.Id, "Вам назначена задача КБ",
                $"{task.Title}.{deadline}", "/Engineering/Index");
        }
        if (previousStatus != EngineeringTaskStatus.Done && task.Status == EngineeringTaskStatus.Done)
            await NotifyTaskCompletedAsync(task, actor, cancellationToken);

        return Success(task);
    }

    public async Task<EngineeringTaskOperationResult> SetPlannedDueAsync(EngineeringTaskActor actor, int id,
        DateOnly? plannedDueOn, CancellationToken cancellationToken = default)
    {
        var task = await _db.EngineeringTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null) return Failure("Задача не найдена.");
        if (!await IsCurrentActiveEngineerAsync(actor, task, cancellationToken))
            return Failure("Плановый срок может изменить только назначенный активный инженер.");

        task.PlannedDueOn = plannedDueOn;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(actor.UserId, actor.UserName, "PlannedDueChanged", "EngineeringTask",
            task.Id.ToString(), $"Плановый срок: {FormatDate(plannedDueOn)}");
        return Success(task);
    }

    public async Task<EngineeringTaskOperationResult> StartAsync(EngineeringTaskActor actor, int id,
        CancellationToken cancellationToken = default)
    {
        var task = await _db.EngineeringTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null) return Failure("Задача не найдена.");
        if (!await IsCurrentActiveEngineerAsync(actor, task, cancellationToken))
            return Failure("Взять задачу в работу может только назначенный активный инженер.");
        if (task.Status != EngineeringTaskStatus.Assigned)
            return Failure("В работу можно взять только задачу со статусом «Назначена».", "Status");

        task.Status = EngineeringTaskStatus.InProgress;
        task.StartedOn ??= DateOnly.FromDateTime(DateTime.Today);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(actor.UserId, actor.UserName, "Started", "EngineeringTask",
            task.Id.ToString(), task.Title);
        return Success(task);
    }

    public async Task<EngineeringTaskOperationResult> CompleteAsync(EngineeringTaskActor actor, int id,
        CancellationToken cancellationToken = default)
    {
        var task = await _db.EngineeringTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null) return Failure("Задача не найдена.");
        if (!await IsCurrentActiveEngineerAsync(actor, task, cancellationToken))
            return Failure("Выполнить задачу может только назначенный активный инженер.");
        if (task.Status != EngineeringTaskStatus.InProgress)
            return Failure("Выполнить можно только задачу со статусом «В работе».", "Status");

        var now = DateTime.Now;
        task.Status = EngineeringTaskStatus.Done;
        task.StartedOn ??= DateOnly.FromDateTime(now);
        task.CompletedOn = DateOnly.FromDateTime(now);
        task.CompletedAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(actor.UserId, actor.UserName, "Completed", "EngineeringTask",
            task.Id.ToString(), task.Title);
        await NotifyTaskCompletedAsync(task, actor, cancellationToken);
        return Success(task);
    }

    public async Task<EngineeringTaskOperationResult> AddCommentAsync(EngineeringTaskActor actor, int id,
        string text, CancellationToken cancellationToken = default)
    {
        if (!HasEngineeringAccess(actor)) return Failure("Недостаточно прав для добавления комментария.");

        var task = await _db.EngineeringTasks.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        if (task is null) return Failure("Задача не найдена.");
        if (string.IsNullOrWhiteSpace(text)) return Failure("Введите текст комментария.", "Text");
        if (text.Length > 4000)
            return Failure("Комментарий не может быть длиннее 4000 символов.", "Text");

        var comment = new EngineeringTaskComment
        {
            EngineeringTaskId = task.Id,
            AuthorUserId = actor.UserId,
            AuthorName = actor.UserName,
            Text = text.Trim(),
            CreatedAt = DateTime.Now,
        };
        _db.EngineeringTaskComments.Add(comment);
        await _db.SaveChangesAsync(cancellationToken);
        await _audit.LogAsync(actor.UserId, actor.UserName, "CommentAdded", "EngineeringTask",
            task.Id.ToString(), comment.Text);
        return Success(task);
    }

    public async Task<EngineeringTaskOperationResult> GetTaskAsync(EngineeringTaskActor actor, int id,
        CancellationToken cancellationToken = default)
    {
        if (!HasEngineeringAccess(actor)) return Failure("Недостаточно прав для просмотра задачи.");

        var task = await _db.EngineeringTasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.Boat)
            .Include(t => t.Comments)
            .Include(t => t.Attachments)
            .FirstOrDefaultAsync(t => t.Id == id, cancellationToken);
        return task is null ? Failure("Задача не найдена.") : Success(task);
    }

    public async Task<List<EngineeringTask>> GetUserTasksAsync(string userId, bool completed,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return new List<EngineeringTask>();

        var query = _db.EngineeringTasks.AsNoTracking()
            .Include(t => t.Assignee)
            .Include(t => t.Boat)
            .Where(t => t.AssigneeId == userId
                && (completed ? t.Status == EngineeringTaskStatus.Done : t.Status != EngineeringTaskStatus.Done));
        return completed
            ? await query.OrderByDescending(t => t.CompletedAt)
                .ThenByDescending(t => t.CompletedOn)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken)
            : await query.OrderByDescending(t => t.Priority)
                .ThenBy(t => t.Deadline == null)
                .ThenBy(t => t.Deadline)
                .ThenByDescending(t => t.CreatedAt)
                .ToListAsync(cancellationToken);
    }

    private async Task NotifyTaskCompletedAsync(EngineeringTask task, EngineeringTaskActor actor,
        CancellationToken cancellationToken)
    {
        var recipientIds = (await _userManager.GetUsersInRoleAsync(AppRoles.ChiefEngineer))
            .Where(u => u.IsActive)
            .Select(u => u.Id)
            .ToHashSet();
        if (!string.IsNullOrWhiteSpace(task.CreatedByUserId)) recipientIds.Add(task.CreatedByUserId);
        if (!string.IsNullOrWhiteSpace(task.AssigneeId)) recipientIds.Add(task.AssigneeId);
        recipientIds.Remove(actor.UserId);

        var recipients = await _db.Users.AsNoTracking()
            .Where(u => recipientIds.Contains(u.Id) && u.IsActive)
            .ToListAsync(cancellationToken);
        if (recipients.Count == 0) return;

        var assignee = task.Assignee ?? await _db.Users.AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == task.AssigneeId, cancellationToken);
        var assigneeName = assignee is null ? actor.UserName : UserName(assignee);
        var message = $"{task.Title}. Исполнитель: {assigneeName}. Завершена: {task.CompletedAt:dd.MM.yyyy HH:mm}.";
        foreach (var recipient in recipients)
        {
            await _notifications.NotifyAsync(recipient.Id, "Задача КБ выполнена", message,
                $"/Engineering/Details/{task.Id}");
        }
    }

    private async Task<EngineeringTaskOperationResult?> ValidateTaskInputAsync(string title,
        EngineeringTaskPriority priority, int? boatId, string? hullNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(title)) return Failure("Укажите название задачи.", "Title");
        if (title.Length > 1000)
            return Failure("Название задачи не может быть длиннее 1000 символов.", "Title");
        if (!Enum.IsDefined(priority)) return Failure("Выберите корректный приоритет.", "Priority");
        if (hullNumber?.Length > 100)
            return Failure("Номер корпуса не может быть длиннее 100 символов.", "HullNumber");
        if (boatId is { } id && !await _db.Boats.AnyAsync(b => b.Id == id && b.IsActive, cancellationToken))
            return Failure("Выберите активный катер из справочника.", "BoatId");
        return null;
    }

    private async Task<ApplicationUser?> GetActiveEngineerAsync(string userId)
        => (await _userManager.GetUsersInRoleAsync(AppRoles.Engineer))
            .FirstOrDefault(u => u.Id == userId && u.IsActive);

    private async Task<bool> IsCurrentActiveEngineerAsync(EngineeringTaskActor actor, EngineeringTask task,
        CancellationToken cancellationToken)
        => actor.IsInRole(AppRoles.Engineer)
            && task.AssigneeId == actor.UserId
            && await _db.Users.AnyAsync(u => u.Id == actor.UserId && u.IsActive, cancellationToken);

    private static bool CanCreate(EngineeringTaskActor actor)
        => actor.IsInRole(AppRoles.ProductionHead)
            || actor.IsInRole(AppRoles.ChiefEngineer)
            || actor.IsInRole(AppRoles.Admin);

    private static bool CanEdit(EngineeringTaskActor actor)
        => actor.IsInRole(AppRoles.ChiefEngineer) || actor.IsInRole(AppRoles.Admin);

    private static bool HasEngineeringAccess(EngineeringTaskActor actor)
        => CanCreate(actor) || actor.IsInRole(AppRoles.Engineer);

    private static bool IsValidTransition(EngineeringTaskStatus current, EngineeringTaskStatus next)
        => next == current || (int)next == (int)current + 1 || (int)next == (int)current - 1;

    private static void ApplyStatusDates(EngineeringTask task, EngineeringTaskStatus previousStatus, DateTime now)
    {
        var today = DateOnly.FromDateTime(now);
        if (task.Status == EngineeringTaskStatus.InProgress && previousStatus != EngineeringTaskStatus.InProgress)
            task.StartedOn ??= today;
        if (task.Status == EngineeringTaskStatus.Done && previousStatus != EngineeringTaskStatus.Done)
        {
            task.StartedOn ??= today;
            task.CompletedOn = today;
            task.CompletedAt = now;
        }
        if (previousStatus == EngineeringTaskStatus.Done && task.Status != EngineeringTaskStatus.Done)
        {
            task.CompletedOn = null;
            task.CompletedAt = null;
        }
        if (previousStatus == EngineeringTaskStatus.InProgress && task.Status < EngineeringTaskStatus.InProgress)
        {
            task.StartedOn = null;
            task.CompletedOn = null;
            task.CompletedAt = null;
        }
        if (task.Status == EngineeringTaskStatus.New)
        {
            task.StartedOn = null;
            task.CompletedOn = null;
            task.CompletedAt = null;
        }
    }

    private static EngineeringTaskOperationResult Success(EngineeringTask task) => new()
    {
        Success = true,
        Task = task,
    };

    private static EngineeringTaskOperationResult Failure(string error, string? field = null) => new()
    {
        Success = false,
        Error = error,
        Field = field,
    };

    private static string PriorityName(EngineeringTaskPriority priority) => priority switch
    {
        EngineeringTaskPriority.Low => "Низкий",
        EngineeringTaskPriority.Normal => "Обычный",
        EngineeringTaskPriority.High => "Высокий",
        EngineeringTaskPriority.Urgent => "Срочный",
        _ => priority.ToString(),
    };

    private static string FormatDate(DateOnly? date) => date?.ToString("dd.MM.yyyy") ?? "не задан";

    private static string UserName(ApplicationUser user)
        => string.IsNullOrWhiteSpace(user.DisplayName) ? user.Email ?? user.Id : user.DisplayName;
}
