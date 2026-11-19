using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Telegram;

public sealed class TelegramUpdateHandler
{
    public const long MaxAttachmentSize = 20L * 1024 * 1024;

    private readonly TelegramLinkService _links;
    private readonly EngineeringTaskService _tasks;
    private readonly EngineeringTaskAttachmentService _attachments;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ITelegramBotApi _api;
    private readonly TelegramConversationStore _conversations;
    private readonly TelegramOptions _options;

    public TelegramUpdateHandler(TelegramLinkService links, EngineeringTaskService tasks,
        EngineeringTaskAttachmentService attachments, UserManager<ApplicationUser> userManager,
        ITelegramBotApi api, TelegramConversationStore conversations, IOptions<TelegramOptions> options)
    {
        _links = links;
        _tasks = tasks;
        _attachments = attachments;
        _userManager = userManager;
        _api = api;
        _conversations = conversations;
        _options = options.Value;
    }

    public async Task HandleAsync(Update update, CancellationToken cancellationToken = default)
    {
        if (update.Message is { } message)
            await HandleMessageAsync(message, cancellationToken);
        else if (update.CallbackQuery is { } callbackQuery)
            await HandleCallbackQueryAsync(callbackQuery, cancellationToken);
    }

    private async Task HandleMessageAsync(Message message, CancellationToken cancellationToken)
    {
        if (message.From is not { } telegramUser || !IsAllowedChat(message.Chat))
            return;

        var text = message.Text?.Trim();
        var command = text?.StartsWith('/') == true ? TelegramText.CommandName(text) : null;
        if (command == "/start")
        {
            await HandleStartAsync(message, telegramUser, text!, cancellationToken);
            return;
        }

        if (command == "/cancel")
        {
            _conversations.Remove(message.Chat.Id, telegramUser.Id);
            await _api.SendMessageAsync(message.Chat.Id, "Текущее действие отменено.",
                cancellationToken: cancellationToken);
            return;
        }

        var actor = await GetEngineeringActorAsync(message.Chat.Id, telegramUser.Id, cancellationToken);
        if (actor is null)
            return;

        switch (command)
        {
            case "/newtask":
                await BeginNewTaskAsync(message, actor, cancellationToken);
                return;
            case "/tasks":
                _conversations.Remove(message.Chat.Id, telegramUser.Id);
                await SendTasksAsync(message.Chat.Id, actor, completed: false, cancellationToken);
                return;
            case "/completed":
                _conversations.Remove(message.Chat.Id, telegramUser.Id);
                await SendTasksAsync(message.Chat.Id, actor, completed: true, cancellationToken);
                return;
        }

        if (text == TelegramKeyboards.NewTask)
        {
            await BeginNewTaskAsync(message, actor, cancellationToken);
            return;
        }
        if (text == TelegramKeyboards.ActiveTasks)
        {
            _conversations.Remove(message.Chat.Id, telegramUser.Id);
            await SendTasksAsync(message.Chat.Id, actor, completed: false, cancellationToken);
            return;
        }
        if (text == TelegramKeyboards.CompletedTasks)
        {
            _conversations.Remove(message.Chat.Id, telegramUser.Id);
            await SendTasksAsync(message.Chat.Id, actor, completed: true, cancellationToken);
            return;
        }

        if (_conversations.TryGet(message.Chat.Id, telegramUser.Id, out var conversation))
        {
            await ContinueConversationAsync(message, actor, conversation, cancellationToken);
            return;
        }

        await SendHelpAsync(message.Chat.Id, actor, cancellationToken);
    }

    private async Task HandleStartAsync(Message message, User telegramUser, string text,
        CancellationToken cancellationToken)
    {
        _conversations.Remove(message.Chat.Id, telegramUser.Id);
        var parts = text.Split(' ', 2, StringSplitOptions.TrimEntries);
        var code = parts.Length == 2 ? parts[1].Trim() : null;
        if (!string.IsNullOrWhiteSpace(code))
        {
            if (message.Chat.Type != ChatType.Private)
            {
                await _api.SendMessageAsync(message.Chat.Id,
                    "Привязку выполните в личном чате с ботом, чтобы не раскрывать одноразовый код.",
                    cancellationToken: cancellationToken);
                return;
            }

            var result = await _links.LinkAsync(telegramUser.Id, telegramUser.Username, code, cancellationToken);
            if (!result.Success || result.User is null)
            {
                await _api.SendMessageAsync(message.Chat.Id,
                    result.Error ?? "Не удалось привязать Telegram. Получите новый код в профиле на сайте.",
                    cancellationToken: cancellationToken);
                return;
            }
        }

        var actor = await LoadActorAsync(telegramUser.Id, cancellationToken);
        if (actor is null)
        {
            var profileUrl = TelegramText.AbsoluteUrl(_options.SiteBaseUrl, "/Identity/Account/Manage");
            var instruction = "Откройте профиль на сайте, создайте одноразовый код и отправьте команду /start КОД.";
            if (profileUrl is not null)
                instruction += $"\nПрофиль: {profileUrl}";
            await _api.SendMessageAsync(message.Chat.Id, instruction, cancellationToken: cancellationToken);
            return;
        }
        if (!actor.User.IsActive)
        {
            await _api.SendMessageAsync(message.Chat.Id, "Учётная запись отключена. Обратитесь к администратору.",
                cancellationToken: cancellationToken);
            return;
        }

        await _api.SendMessageAsync(message.Chat.Id,
            $"Telegram привязан: {actor.UserName}. Используйте кнопки ниже для работы с задачами КБ.",
            MainMenu(actor, message.Chat.Id), cancellationToken);
    }

    private async Task HandleCallbackQueryAsync(CallbackQuery query, CancellationToken cancellationToken)
    {
        if (query.Message is not { } message || string.IsNullOrWhiteSpace(query.Data))
        {
            await _api.AnswerCallbackQueryAsync(query.Id, "Сообщение больше недоступно.", true, cancellationToken);
            return;
        }
        if (!IsAllowedChat(message.Chat))
        {
            await _api.AnswerCallbackQueryAsync(query.Id, "Чат не разрешён.", true, cancellationToken);
            return;
        }
        if (query.Data == "cancel")
        {
            _conversations.Remove(message.Chat.Id, query.From.Id);
            await _api.AnswerCallbackQueryAsync(query.Id, "Действие отменено.", cancellationToken: cancellationToken);
            return;
        }

        var actor = await GetEngineeringActorAsync(message.Chat.Id, query.From.Id, cancellationToken);
        if (actor is null)
        {
            await _api.AnswerCallbackQueryAsync(query.Id, "Нет доступа.", true, cancellationToken);
            return;
        }

        var parts = query.Data.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var taskId) || taskId <= 0)
        {
            await _api.AnswerCallbackQueryAsync(query.Id, "Неизвестное действие.", true, cancellationToken);
            return;
        }

        switch (parts[0])
        {
            case "task":
                await _api.AnswerCallbackQueryAsync(query.Id, cancellationToken: cancellationToken);
                await ShowTaskAsync(message.Chat.Id, actor, taskId, cancellationToken);
                return;
            case "start":
            {
                var result = await _tasks.StartAsync(actor, taskId, cancellationToken);
                await _api.AnswerCallbackQueryAsync(query.Id, ResultText(result), !result.Success, cancellationToken);
                if (result.Success) await ShowTaskAsync(message.Chat.Id, actor, taskId, cancellationToken);
                return;
            }
            case "complete":
            {
                var result = await _tasks.CompleteAsync(actor, taskId, cancellationToken);
                await _api.AnswerCallbackQueryAsync(query.Id, ResultText(result), !result.Success, cancellationToken);
                if (result.Success) await ShowTaskAsync(message.Chat.Id, actor, taskId, cancellationToken);
                return;
            }
            case "plan":
            case "comment":
            case "attachment":
            {
                var result = await _tasks.GetTaskAsync(actor, taskId, cancellationToken);
                if (!result.Success || result.Task is null)
                {
                    await _api.AnswerCallbackQueryAsync(query.Id, ResultText(result), true, cancellationToken);
                    return;
                }

                if (parts[0] == "plan" && result.Task.Status == EngineeringTaskStatus.Done)
                {
                    await _api.AnswerCallbackQueryAsync(query.Id,
                        "Плановый срок выполненной задачи изменять нельзя.", true, cancellationToken);
                    return;
                }

                var step = parts[0] switch
                {
                    "plan" => TelegramConversationStep.PlannedDue,
                    "comment" => TelegramConversationStep.Comment,
                    _ => TelegramConversationStep.Attachment,
                };
                _conversations.Set(message.Chat.Id, query.From.Id, new TelegramConversation(step, taskId));
                await _api.AnswerCallbackQueryAsync(query.Id, cancellationToken: cancellationToken);
                var prompt = step switch
                {
                    TelegramConversationStep.PlannedDue =>
                        "Введите плановый срок в формате ДД.ММ.ГГГГ или «пропустить», чтобы очистить срок.",
                    TelegramConversationStep.Comment => "Введите комментарий к задаче.",
                    _ => "Отправьте документ или фотографию размером до 20 МиБ.",
                };
                await _api.SendMessageAsync(message.Chat.Id, prompt, TelegramKeyboards.Cancel(), cancellationToken);
                return;
            }
            default:
                await _api.AnswerCallbackQueryAsync(query.Id, "Неизвестное действие.", true, cancellationToken);
                return;
        }
    }

    private async Task BeginNewTaskAsync(Message message, EngineeringTelegramActor actor,
        CancellationToken cancellationToken)
    {
        if (!CanCreate(actor) || _options.EngineeringChatId != message.Chat.Id)
        {
            await _api.SendMessageAsync(message.Chat.Id,
                "Создавать задачи через Telegram могут начальник производства, главный инженер и администратор в настроенном инженерном чате.",
                cancellationToken: cancellationToken);
            return;
        }

        _conversations.Set(message.Chat.Id, actor.TelegramUserId,
            new TelegramConversation(TelegramConversationStep.NewTaskTitle));
        await _api.SendMessageAsync(message.Chat.Id, "Шаг 1 из 3. Введите описание задачи.",
            TelegramKeyboards.Cancel(), cancellationToken);
    }

    private async Task ContinueConversationAsync(Message message, EngineeringTelegramActor actor,
        TelegramConversation conversation, CancellationToken cancellationToken)
    {
        var chatId = message.Chat.Id;
        var telegramUserId = actor.TelegramUserId;
        var text = message.Text?.Trim();
        switch (conversation.Step)
        {
            case TelegramConversationStep.NewTaskTitle:
                if (string.IsNullOrWhiteSpace(text) || text.Length > 1000)
                {
                    await _api.SendMessageAsync(chatId, "Введите описание задачи длиной от 1 до 1000 символов.",
                        TelegramKeyboards.Cancel(), cancellationToken);
                    return;
                }
                _conversations.Set(chatId, telegramUserId,
                    new TelegramConversation(TelegramConversationStep.NewTaskDeadline, Title: text));
                await _api.SendMessageAsync(chatId,
                    "Шаг 2 из 3. Введите дедлайн ДД.ММ.ГГГГ или «пропустить».",
                    TelegramKeyboards.Cancel(), cancellationToken);
                return;

            case TelegramConversationStep.NewTaskDeadline:
                if (!TelegramText.TryParseOptionalDate(text, out var deadline))
                {
                    await _api.SendMessageAsync(chatId, "Неверная дата. Используйте формат ДД.ММ.ГГГГ или «пропустить».",
                        TelegramKeyboards.Cancel(), cancellationToken);
                    return;
                }
                _conversations.Set(chatId, telegramUserId,
                    new TelegramConversation(TelegramConversationStep.NewTaskHullNumber,
                        Title: conversation.Title, Deadline: deadline));
                await _api.SendMessageAsync(chatId,
                    "Шаг 3 из 3. Введите номер катера или «пропустить».",
                    TelegramKeyboards.Cancel(), cancellationToken);
                return;

            case TelegramConversationStep.NewTaskHullNumber:
            {
                if (!CanCreate(actor) || _options.EngineeringChatId != chatId)
                {
                    _conversations.Remove(chatId, telegramUserId);
                    await _api.SendMessageAsync(chatId, "Создание задачи в этом чате недоступно.",
                        cancellationToken: cancellationToken);
                    return;
                }
                var hullNumber = TelegramText.IsSkip(text) ? null : text;
                if (hullNumber?.Length > 100)
                {
                    await _api.SendMessageAsync(chatId, "Номер катера не может быть длиннее 100 символов.",
                        TelegramKeyboards.Cancel(), cancellationToken);
                    return;
                }
                var result = await _tasks.CreateAsync(actor, conversation.Title ?? string.Empty,
                    EngineeringTaskPriority.Normal, null, hullNumber, conversation.Deadline, cancellationToken);
                if (!result.Success || result.Task is null)
                {
                    await _api.SendMessageAsync(chatId, ResultText(result), TelegramKeyboards.Cancel(), cancellationToken);
                    return;
                }
                _conversations.Remove(chatId, telegramUserId);
                await _api.SendMessageAsync(chatId, $"Задача №{result.Task.Id} создана.\n{TaskDetails(result.Task)}",
                    TelegramKeyboards.TaskDetails(result.Task, actor, _options.SiteBaseUrl), cancellationToken);
                return;
            }

            case TelegramConversationStep.PlannedDue when conversation.TaskId is { } plannedTaskId:
                if (!TelegramText.TryParseOptionalDate(text, out var plannedDueOn))
                {
                    await _api.SendMessageAsync(chatId, "Неверная дата. Используйте формат ДД.ММ.ГГГГ или «пропустить».",
                        TelegramKeyboards.Cancel(), cancellationToken);
                    return;
                }
                var plannedResult = await _tasks.SetPlannedDueAsync(actor, plannedTaskId, plannedDueOn, cancellationToken);
                if (!plannedResult.Success)
                {
                    await _api.SendMessageAsync(chatId, ResultText(plannedResult), TelegramKeyboards.Cancel(), cancellationToken);
                    return;
                }
                _conversations.Remove(chatId, telegramUserId);
                await _api.SendMessageAsync(chatId, "Плановый срок сохранён.", cancellationToken: cancellationToken);
                await ShowTaskAsync(chatId, actor, plannedTaskId, cancellationToken);
                return;

            case TelegramConversationStep.Comment when conversation.TaskId is { } commentTaskId:
                if (string.IsNullOrWhiteSpace(text))
                {
                    await _api.SendMessageAsync(chatId, "Введите текст комментария.",
                        TelegramKeyboards.Cancel(), cancellationToken);
                    return;
                }
                var commentResult = await _tasks.AddCommentAsync(actor, commentTaskId, text, cancellationToken);
                if (!commentResult.Success)
                {
                    await _api.SendMessageAsync(chatId, ResultText(commentResult), TelegramKeyboards.Cancel(), cancellationToken);
                    return;
                }
                _conversations.Remove(chatId, telegramUserId);
                await _api.SendMessageAsync(chatId, "Комментарий добавлен.", cancellationToken: cancellationToken);
                await ShowTaskAsync(chatId, actor, commentTaskId, cancellationToken);
                return;

            case TelegramConversationStep.Attachment when conversation.TaskId is { } attachmentTaskId:
                await SaveAttachmentAsync(message, actor, attachmentTaskId, cancellationToken);
                return;

            default:
                _conversations.Remove(chatId, telegramUserId);
                await SendHelpAsync(chatId, actor, cancellationToken);
                return;
        }
    }

    private async Task SaveAttachmentAsync(Message message, EngineeringTelegramActor actor, int taskId,
        CancellationToken cancellationToken)
    {
        string fileId;
        string fileName;
        string contentType;
        long? declaredSize;
        if (message.Document is { } document)
        {
            fileId = document.FileId;
            fileName = string.IsNullOrWhiteSpace(document.FileName) ? "document" : document.FileName;
            contentType = string.IsNullOrWhiteSpace(document.MimeType)
                ? "application/octet-stream"
                : document.MimeType;
            declaredSize = document.FileSize;
        }
        else if (message.Photo?.LastOrDefault() is { } photo)
        {
            fileId = photo.FileId;
            fileName = $"photo_{photo.FileUniqueId}.jpg";
            contentType = "image/jpeg";
            declaredSize = photo.FileSize;
        }
        else
        {
            await _api.SendMessageAsync(message.Chat.Id, "Отправьте документ или фотографию размером до 20 МиБ.",
                TelegramKeyboards.Cancel(), cancellationToken);
            return;
        }

        if (declaredSize > MaxAttachmentSize)
        {
            await _api.SendMessageAsync(message.Chat.Id, "Файл превышает ограничение 20 МиБ.",
                TelegramKeyboards.Cancel(), cancellationToken);
            return;
        }

        using var content = new MemoryStream(declaredSize is > 0 and <= MaxAttachmentSize ? (int)declaredSize.Value : 0);
        await _api.DownloadFileAsync(fileId, content, cancellationToken);
        content.Position = 0;
        var result = await _attachments.SaveAsync(actor, taskId, fileName, contentType, content, content.Length,
            cancellationToken);
        if (!result.Success)
        {
            await _api.SendMessageAsync(message.Chat.Id, ResultText(result), TelegramKeyboards.Cancel(), cancellationToken);
            return;
        }

        _conversations.Remove(message.Chat.Id, actor.TelegramUserId);
        await _api.SendMessageAsync(message.Chat.Id, "Вложение добавлено.", cancellationToken: cancellationToken);
        await ShowTaskAsync(message.Chat.Id, actor, taskId, cancellationToken);
    }

    private async Task SendTasksAsync(long chatId, EngineeringTaskActor actor, bool completed,
        CancellationToken cancellationToken)
    {
        var tasks = await _tasks.GetUserTasksAsync(actor.UserId, completed, cancellationToken);
        if (tasks.Count == 0)
        {
            await _api.SendMessageAsync(chatId,
                completed ? "У вас пока нет выполненных задач." : "У вас нет активных задач.",
                cancellationToken: cancellationToken);
            return;
        }

        foreach (var task in tasks.Take(20))
        {
            await _api.SendMessageAsync(chatId, TaskSummary(task), TelegramKeyboards.TaskListItem(task.Id),
                cancellationToken);
        }
        if (tasks.Count > 20)
            await _api.SendMessageAsync(chatId, "Показаны первые 20 задач. Остальные доступны на сайте.",
                cancellationToken: cancellationToken);
    }

    private async Task ShowTaskAsync(long chatId, EngineeringTaskActor actor, int taskId,
        CancellationToken cancellationToken)
    {
        var result = await _tasks.GetTaskAsync(actor, taskId, cancellationToken);
        if (!result.Success || result.Task is null)
        {
            await _api.SendMessageAsync(chatId, ResultText(result), cancellationToken: cancellationToken);
            return;
        }

        await _api.SendMessageAsync(chatId, TaskDetails(result.Task),
            TelegramKeyboards.TaskDetails(result.Task, actor, _options.SiteBaseUrl), cancellationToken);
    }

    private async Task SendHelpAsync(long chatId, EngineeringTaskActor actor, CancellationToken cancellationToken)
    {
        var text = "Доступные команды:\n/tasks — мои активные задачи\n/completed — выполненные задачи\n/newtask — создать задачу в инженерном чате\n/cancel — отменить текущее действие";
        await _api.SendMessageAsync(chatId, text, MainMenu(actor, chatId), cancellationToken);
    }

    private ReplyMarkup MainMenu(EngineeringTaskActor actor, long chatId)
        => TelegramKeyboards.MainMenu(CanCreate(actor) && _options.EngineeringChatId == chatId);

    private async Task<EngineeringTelegramActor?> GetEngineeringActorAsync(long chatId, long telegramUserId,
        CancellationToken cancellationToken)
    {
        var actor = await LoadActorAsync(telegramUserId, cancellationToken);
        if (actor is null)
        {
            await _api.SendMessageAsync(chatId,
                "Telegram не привязан. Создайте одноразовый код в профиле на сайте и отправьте /start КОД.",
                cancellationToken: cancellationToken);
            return null;
        }
        if (!actor.User.IsActive || !HasEngineeringAccess(actor))
        {
            await _api.SendMessageAsync(chatId, "У вас нет доступа к задачам КБ.", cancellationToken: cancellationToken);
            return null;
        }
        return actor;
    }

    private async Task<EngineeringTelegramActor?> LoadActorAsync(long telegramUserId,
        CancellationToken cancellationToken)
    {
        var user = await _links.GetUserAsync(telegramUserId, cancellationToken);
        if (user is null)
            return null;
        var roles = await _userManager.GetRolesAsync(user);
        var userName = !string.IsNullOrWhiteSpace(user.DisplayName)
            ? user.DisplayName
            : user.UserName ?? user.Email ?? user.Id;
        return new EngineeringTelegramActor(telegramUserId, user,
            new EngineeringTaskActor(user.Id, userName, roles.ToArray()));
    }

    private bool IsAllowedChat(Chat chat)
        => chat.Type == ChatType.Private || _options.EngineeringChatId == chat.Id;

    private static bool CanCreate(EngineeringTaskActor actor)
        => actor.IsInRole(AppRoles.ProductionHead) || actor.IsInRole(AppRoles.ChiefEngineer)
            || actor.IsInRole(AppRoles.Admin);

    private static bool HasEngineeringAccess(EngineeringTaskActor actor)
        => actor.IsInRole(AppRoles.Engineer) || CanCreate(actor);

    private static string ResultText(EngineeringTaskOperationResult result)
        => result.Success ? "Готово." : result.Error ?? "Действие недоступно.";

    private static string TaskSummary(EngineeringTask task)
        => $"№{task.Id}. {task.Title}\n{TelegramText.Status(task.Status)} · приоритет: {TelegramText.Priority(task.Priority)}\nДедлайн: {TelegramText.FormatDate(task.Deadline)} · плановый срок: {TelegramText.FormatDate(task.PlannedDueOn)}";

    private static string TaskDetails(EngineeringTask task)
    {
        var text = new StringBuilder();
        text.AppendLine($"Задача №{task.Id}: {task.Title}");
        text.AppendLine($"Статус: {TelegramText.Status(task.Status)}");
        text.AppendLine($"Приоритет: {TelegramText.Priority(task.Priority)}");
        text.AppendLine($"Автор: {task.CreatedByName}");
        text.AppendLine($"Исполнитель: {UserName(task.Assignee)}");
        text.AppendLine($"Дедлайн: {TelegramText.FormatDate(task.Deadline)}");
        text.AppendLine($"Плановый срок: {TelegramText.FormatDate(task.PlannedDueOn)}");
        if (!string.IsNullOrWhiteSpace(task.HullNumber))
            text.AppendLine($"Номер катера: {task.HullNumber}");
        else if (task.Boat is not null)
            text.AppendLine($"Катер: {task.Boat.Number}");
        if (task.StartedOn is not null)
            text.AppendLine($"Начата: {TelegramText.FormatDate(task.StartedOn)}");
        if (task.CompletedOn is not null)
            text.AppendLine($"Завершена: {TelegramText.FormatDate(task.CompletedOn)}");
        return text.ToString().TrimEnd();
    }

    private static string UserName(ApplicationUser? user)
    {
        if (user is null) return "не назначен";
        return !string.IsNullOrWhiteSpace(user.DisplayName) ? user.DisplayName : user.UserName ?? user.Id;
    }

    private sealed record EngineeringTelegramActor(long TelegramUserId, ApplicationUser User,
        EngineeringTaskActor Actor)
    {
        public string UserId => Actor.UserId;
        public string UserName => Actor.UserName;
        public bool IsInRole(string role) => Actor.IsInRole(role);
        public static implicit operator EngineeringTaskActor(EngineeringTelegramActor actor) => actor.Actor;
    }
}
