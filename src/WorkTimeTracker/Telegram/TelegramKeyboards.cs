using Telegram.Bot.Types.ReplyMarkups;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Telegram;

public static class TelegramKeyboards
{
    public const string ActiveTasks = "Мои активные задачи";
    public const string CompletedTasks = "Выполненные задачи";
    public const string NewTask = "Новая задача";

    public static ReplyKeyboardMarkup MainMenu(bool canCreate)
    {
        var rows = new List<IEnumerable<KeyboardButton>>
        {
            new[] { new KeyboardButton(ActiveTasks), new KeyboardButton(CompletedTasks) },
        };
        if (canCreate)
            rows.Add(new[] { new KeyboardButton(NewTask) });
        return new ReplyKeyboardMarkup(rows) { ResizeKeyboard = true };
    }

    public static InlineKeyboardMarkup Cancel()
        => new(InlineKeyboardButton.WithCallbackData("Отмена", "cancel"));

    public static InlineKeyboardMarkup TaskListItem(int taskId)
        => new(InlineKeyboardButton.WithCallbackData("Открыть задачу", $"task:{taskId}"));

    public static InlineKeyboardMarkup TaskDetails(EngineeringTask task, EngineeringTaskActor actor,
        string? siteBaseUrl)
    {
        var rows = new List<IEnumerable<InlineKeyboardButton>>();
        var isCurrentEngineer = actor.IsInRole(AppRoles.Engineer)
            && task.AssigneeId == actor.UserId
            && task.Status != EngineeringTaskStatus.Done;

        if (isCurrentEngineer)
        {
            if (task.Status == EngineeringTaskStatus.Assigned)
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Взять в работу", $"start:{task.Id}") });
            if (task.Status == EngineeringTaskStatus.InProgress)
                rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Выполнить", $"complete:{task.Id}") });
            rows.Add(new[] { InlineKeyboardButton.WithCallbackData("Плановый срок", $"plan:{task.Id}") });
        }

        rows.Add(new[]
        {
            InlineKeyboardButton.WithCallbackData("Комментарий", $"comment:{task.Id}"),
            InlineKeyboardButton.WithCallbackData("Вложение", $"attachment:{task.Id}"),
        });

        var url = TelegramText.TaskUrl(siteBaseUrl, task.Id);
        if (url is not null)
            rows.Add(new[] { InlineKeyboardButton.WithUrl("Открыть на сайте", url) });

        return new InlineKeyboardMarkup(rows);
    }
}
