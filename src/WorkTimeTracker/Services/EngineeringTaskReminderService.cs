using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

public class EngineeringTaskReminderService
{
    private const string TaskListLink = "/Engineering/Index";
    private readonly ApplicationDbContext _db;
    private readonly NotificationService _notifications;
    private readonly UserManager<ApplicationUser> _userManager;

    public EngineeringTaskReminderService(ApplicationDbContext db, NotificationService notifications,
        UserManager<ApplicationUser> userManager)
    {
        _db = db;
        _notifications = notifications;
        _userManager = userManager;
    }

    public async Task<int> SendRemindersAsync(DateOnly today)
    {
        var tasks = await _db.EngineeringTasks
            .Where(t => t.Status != EngineeringTaskStatus.Done && t.Deadline != null &&
                        t.Deadline <= today.AddDays(2))
            .OrderBy(t => t.Deadline)
            .ThenBy(t => t.Id)
            .ToListAsync();
        if (tasks.Count == 0) return 0;

        var chiefEngineerIds = (await _userManager.GetUsersInRoleAsync(AppRoles.ChiefEngineer))
            .Where(u => u.IsActive)
            .Select(u => u.Id)
            .ToHashSet();
        var sent = 0;

        foreach (var task in tasks)
        {
            var deadline = task.Deadline!.Value;
            var recipientIds = new HashSet<string>(chiefEngineerIds);
            if (!string.IsNullOrWhiteSpace(task.AssigneeId))
                recipientIds.Add(task.AssigneeId);
            if (recipientIds.Count == 0) continue;

            if (deadline >= today && task.DeadlineReminderSentOn != deadline)
            {
                var title = deadline == today
                    ? "Сегодня дедлайн задачи КБ"
                    : "Приближается дедлайн задачи КБ";
                var message = $"{task.Title}. Дедлайн: {deadline:dd.MM.yyyy}.";
                foreach (var recipientId in recipientIds)
                {
                    await _notifications.NotifyAsync(recipientId, title, message, TaskListLink);
                    sent++;
                }

                task.DeadlineReminderSentOn = deadline;
                await _db.SaveChangesAsync();
            }

            if (deadline < today && task.OverdueReminderSentOn != deadline)
            {
                var overdueDays = today.DayNumber - deadline.DayNumber;
                var message = $"{task.Title}. Дедлайн: {deadline:dd.MM.yyyy}; просрочка: {overdueDays} дн.";
                foreach (var recipientId in recipientIds)
                {
                    await _notifications.NotifyAsync(recipientId, "Просрочен дедлайн задачи КБ", message,
                        TaskListLink);
                    sent++;
                }

                task.OverdueReminderSentOn = deadline;
                await _db.SaveChangesAsync();
            }
        }

        return sent;
    }
}
