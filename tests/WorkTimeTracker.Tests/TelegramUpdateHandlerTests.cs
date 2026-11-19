using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;
using WorkTimeTracker.Telegram;
using Xunit;

namespace WorkTimeTracker.Tests;

public class TelegramUpdateHandlerTests : IDisposable
{
    private const long EngineeringChatId = -100500;
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;
    private readonly ServiceProvider _services;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly FakeTelegramBotApi _api = new();
    private readonly TelegramUpdateHandler _handler;
    private readonly string _contentRoot;

    public TelegramUpdateHandlerTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(_db);
        services.AddIdentityCore<ApplicationUser>()
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<ApplicationDbContext>();
        _services = services.BuildServiceProvider();
        _userManager = _services.GetRequiredService<UserManager<ApplicationUser>>();
        _roleManager = _services.GetRequiredService<RoleManager<IdentityRole>>();

        _contentRoot = Path.Combine(Directory.GetCurrentDirectory(), "TestResults",
            "telegram-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_contentRoot);
        var environment = new TestHostEnvironment { ContentRootPath = _contentRoot };
        var telegramOptions = Options.Create(new TelegramOptions
        {
            BotToken = "test-token",
            EngineeringChatId = EngineeringChatId,
            SiteBaseUrl = "https://worktime.example.test",
        });
        _handler = new TelegramUpdateHandler(
            new TelegramLinkService(_db),
            new EngineeringTaskService(_db, _userManager, new NotificationService(_db), new AuditService(_db)),
            new EngineeringTaskAttachmentService(_db, new AuditService(_db), environment),
            _userManager, _api, new TelegramConversationStore(), telegramOptions);
    }

    [Fact]
    public async Task NewTask_InEngineeringChat_CreatesTaskWithTelegramAuthorDeadlineAndHullNumber()
    {
        var creator = await CreateUserAsync("chief", AppRoles.ChiefEngineer, telegramUserId: 100);

        await HandleTextAsync(EngineeringChatId, 100, "/newtask", ChatType.Supergroup);
        await HandleTextAsync(EngineeringChatId, 100, "Разработать чертёж", ChatType.Supergroup);
        await HandleTextAsync(EngineeringChatId, 100, "31.12.2026", ChatType.Supergroup);
        await HandleTextAsync(EngineeringChatId, 100, "К-77", ChatType.Supergroup);

        var task = Assert.Single(_db.EngineeringTasks);
        Assert.Equal(creator.Id, task.CreatedByUserId);
        Assert.Equal("chief", task.CreatedByName);
        Assert.Equal("Разработать чертёж", task.Title);
        Assert.Equal(new DateOnly(2026, 12, 31), task.Deadline);
        Assert.Equal("К-77", task.HullNumber);
        Assert.Equal(EngineeringTaskStatus.New, task.Status);
        Assert.Contains(_api.Messages, m => m.Text.Contains($"Задача №{task.Id} создана."));
    }

    [Fact]
    public async Task NewTask_InPrivateChat_IsRejected()
    {
        await CreateUserAsync("chief", AppRoles.ChiefEngineer, telegramUserId: 100);

        await HandleTextAsync(100, 100, "/newtask");

        Assert.Empty(_db.EngineeringTasks);
        Assert.Contains(_api.Messages, m => m.Text.Contains("в настроенном инженерном чате"));
    }

    [Fact]
    public async Task CompleteCallback_FromAnotherEngineer_IsRejectedAndDoesNotChangeTask()
    {
        await CreateUserAsync("attacker", AppRoles.Engineer, telegramUserId: 100);
        var assignee = await CreateUserAsync("assignee", AppRoles.Engineer, telegramUserId: 200);
        var task = await CreateTaskAsync(assignee.Id, EngineeringTaskStatus.InProgress);

        await HandleCallbackAsync(100, 100, $"complete:{task.Id}");

        Assert.Equal(EngineeringTaskStatus.InProgress, (await _db.EngineeringTasks.SingleAsync()).Status);
        var answer = Assert.Single(_api.CallbackAnswers);
        Assert.True(answer.ShowAlert);
        Assert.Contains("только назначенный активный инженер", answer.Text);
    }

    [Fact]
    public async Task StartAndCompleteCallbacks_ForCurrentEngineer_UpdateLifecycle()
    {
        var engineer = await CreateUserAsync("engineer", AppRoles.Engineer, telegramUserId: 100);
        var task = await CreateTaskAsync(engineer.Id, EngineeringTaskStatus.Assigned);

        await HandleCallbackAsync(100, 100, $"start:{task.Id}");
        await HandleCallbackAsync(100, 100, $"complete:{task.Id}");

        var completed = await _db.EngineeringTasks.SingleAsync();
        Assert.Equal(EngineeringTaskStatus.Done, completed.Status);
        Assert.NotNull(completed.StartedOn);
        Assert.NotNull(completed.CompletedOn);
        Assert.NotNull(completed.CompletedAt);
    }

    [Fact]
    public async Task PlannedDueAndCommentCallbacks_UseConversationAndSharedServices()
    {
        var engineer = await CreateUserAsync("engineer", AppRoles.Engineer, telegramUserId: 100);
        var task = await CreateTaskAsync(engineer.Id, EngineeringTaskStatus.Assigned);

        await HandleCallbackAsync(100, 100, $"plan:{task.Id}");
        await HandleTextAsync(100, 100, "20.11.2026");
        await HandleCallbackAsync(100, 100, $"comment:{task.Id}");
        await HandleTextAsync(100, 100, "Чертёж передан на проверку");

        Assert.Equal(new DateOnly(2026, 11, 20), (await _db.EngineeringTasks.SingleAsync()).PlannedDueOn);
        var comment = Assert.Single(_db.EngineeringTaskComments);
        Assert.Equal(engineer.Id, comment.AuthorUserId);
        Assert.Equal("Чертёж передан на проверку", comment.Text);
    }

    [Fact]
    public async Task PlannedDueCallback_ForCompletedTask_IsRejected()
    {
        var engineer = await CreateUserAsync("engineer", AppRoles.Engineer, telegramUserId: 100);
        var task = await CreateTaskAsync(engineer.Id, EngineeringTaskStatus.Done);

        await HandleCallbackAsync(100, 100, $"plan:{task.Id}");
        await HandleTextAsync(100, 100, "20.11.2026");

        Assert.Null((await _db.EngineeringTasks.SingleAsync()).PlannedDueOn);
        Assert.Contains(_api.CallbackAnswers, a => a.ShowAlert && a.Text.Contains("выполненной задачи"));
    }

    [Fact]
    public async Task AttachmentCallback_DownloadsThroughAbstractApiAndSavesDocument()
    {
        var engineer = await CreateUserAsync("engineer", AppRoles.Engineer, telegramUserId: 100);
        var task = await CreateTaskAsync(engineer.Id, EngineeringTaskStatus.Assigned);
        _api.FileContent = new byte[] { 1, 2, 3 };

        await HandleCallbackAsync(100, 100, $"attachment:{task.Id}");
        var documentMessage = TextMessage(100, 100, null);
        documentMessage.Document = new Document
        {
            FileId = "telegram-file",
            FileUniqueId = "unique-file",
            FileName = "drawing.pdf",
            MimeType = "application/pdf",
            FileSize = 3,
        };
        await _handler.HandleAsync(new Update { Id = 2, Message = documentMessage });

        var attachment = Assert.Single(_db.EngineeringTaskAttachments);
        Assert.Equal(task.Id, attachment.EngineeringTaskId);
        Assert.Equal("drawing.pdf", attachment.FileName);
        Assert.Equal(3, attachment.Size);
        Assert.Equal("telegram-file", _api.DownloadedFileId);
        Assert.True(File.Exists(Path.Combine(_contentRoot, "App_Data", "engineering-attachments",
            attachment.StoredFileName)));
    }

    private async Task<ApplicationUser> CreateUserAsync(string userName, string role, long telegramUserId)
    {
        if (!await _roleManager.RoleExistsAsync(role))
            Assert.True((await _roleManager.CreateAsync(new IdentityRole(role))).Succeeded);
        var user = new ApplicationUser
        {
            UserName = userName,
            Email = $"{userName}@example.test",
            DisplayName = userName,
            TelegramUserId = telegramUserId,
        };
        Assert.True((await _userManager.CreateAsync(user)).Succeeded);
        Assert.True((await _userManager.AddToRoleAsync(user, role)).Succeeded);
        return user;
    }

    private async Task<EngineeringTask> CreateTaskAsync(string assigneeId, EngineeringTaskStatus status)
    {
        var task = new EngineeringTask
        {
            CreatedByUserId = "creator",
            CreatedByName = "Главный инженер",
            Title = "Тестовая задача",
            AssigneeId = assigneeId,
            Status = status,
            StartedOn = status == EngineeringTaskStatus.InProgress ? DateOnly.FromDateTime(DateTime.Today) : null,
        };
        _db.EngineeringTasks.Add(task);
        await _db.SaveChangesAsync();
        return task;
    }

    private Task HandleTextAsync(long chatId, long userId, string text, ChatType chatType = ChatType.Private)
        => _handler.HandleAsync(new Update { Id = 1, Message = TextMessage(chatId, userId, text, chatType) });

    private Task HandleCallbackAsync(long chatId, long userId, string data)
        => _handler.HandleAsync(new Update
        {
            Id = 1,
            CallbackQuery = new CallbackQuery
            {
                Id = Guid.NewGuid().ToString("N"),
                From = TelegramUser(userId),
                Message = TextMessage(chatId, userId, null),
                Data = data,
            },
        });

    private static Message TextMessage(long chatId, long userId, string? text,
        ChatType chatType = ChatType.Private)
        => new()
        {
            Date = DateTime.UtcNow,
            Chat = new Chat { Id = chatId, Type = chatType },
            From = TelegramUser(userId),
            Text = text,
        };

    private static global::Telegram.Bot.Types.User TelegramUser(long userId)
        => new() { Id = userId, FirstName = "Тест", Username = $"telegram_{userId}" };

    public void Dispose()
    {
        _services.Dispose();
        _db.Dispose();
        _connection.Dispose();
        try
        {
            if (Directory.Exists(_contentRoot)) Directory.Delete(_contentRoot, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";
        public string ApplicationName { get; set; } = "WorkTimeTracker.Tests";
        public string ContentRootPath { get; set; } = string.Empty;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private sealed class FakeTelegramBotApi : ITelegramBotApi
    {
        public bool IsEnabled => true;
        public List<(long ChatId, string Text, ReplyMarkup? ReplyMarkup)> Messages { get; } = new();
        public List<(string Text, bool ShowAlert)> CallbackAnswers { get; } = new();
        public byte[] FileContent { get; set; } = Array.Empty<byte>();
        public string? DownloadedFileId { get; private set; }

        public Task SendMessageAsync(long chatId, string text, ReplyMarkup? replyMarkup = null,
            CancellationToken cancellationToken = default)
        {
            Messages.Add((chatId, text, replyMarkup));
            return Task.CompletedTask;
        }

        public Task AnswerCallbackQueryAsync(string callbackQueryId, string? text = null, bool showAlert = false,
            CancellationToken cancellationToken = default)
        {
            CallbackAnswers.Add((text ?? string.Empty, showAlert));
            return Task.CompletedTask;
        }

        public async Task DownloadFileAsync(string fileId, Stream destination,
            CancellationToken cancellationToken = default)
        {
            DownloadedFileId = fileId;
            await destination.WriteAsync(FileContent, cancellationToken);
        }
    }
}
