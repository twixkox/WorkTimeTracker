using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;
using WorkTimeTracker.Pages.Notifications;
using WorkTimeTracker.Services;
using Xunit;

namespace WorkTimeTracker.Tests;

public class NotificationCenterTests : IDisposable
{
    private const string UserId = "user";
    private readonly SqliteConnection _connection;
    private readonly ApplicationDbContext _db;

    public NotificationCenterTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite(_connection)
            .Options;
        _db = new ApplicationDbContext(options);
        _db.Database.EnsureCreated();
    }

    [Fact]
    public async Task OnGet_AppliesFiltersWithoutMarkingNotificationsRead()
    {
        _db.Notifications.AddRange(
            new Notification { UserId = UserId, Title = "КБ", Category = NotificationCategory.Engineering },
            new Notification { UserId = UserId, Title = "Табель", Category = NotificationCategory.Timesheet },
            new Notification
            {
                UserId = UserId,
                Title = "Прочитанная КБ",
                Category = NotificationCategory.Engineering,
                IsRead = true,
            });
        await _db.SaveChangesAsync();
        var page = CreatePage();
        page.Category = NotificationCategory.Engineering;

        var result = await page.OnGetAsync();

        Assert.IsType<Microsoft.AspNetCore.Mvc.RazorPages.PageResult>(result);
        Assert.Equal("КБ", Assert.Single(page.Items).Title);
        Assert.Equal(2, page.UnreadCount);
        Assert.Equal(2, await _db.Notifications.CountAsync(n => !n.IsRead));
    }

    [Fact]
    public async Task OnPostMarkReadAndMarkAllRead_UpdateOnlyThroughPostHandlers()
    {
        _db.Notifications.AddRange(
            new Notification { UserId = UserId, Title = "Первое" },
            new Notification { UserId = UserId, Title = "Второе" });
        await _db.SaveChangesAsync();
        var firstId = await _db.Notifications.Select(n => n.Id).FirstAsync();
        var page = CreatePage();

        var singleResult = await page.OnPostMarkReadAsync(firstId, "unread", null);
        Assert.IsType<RedirectToPageResult>(singleResult);
        Assert.Equal(1, await _db.Notifications.CountAsync(n => n.IsRead));

        var allResult = await page.OnPostMarkAllReadAsync("unread", null);
        Assert.IsType<RedirectToPageResult>(allResult);
        Assert.Equal(0, await _db.Notifications.CountAsync(n => !n.IsRead));
    }

    private IndexModel CreatePage()
    {
        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, UserId)], "Test")),
        };
        var currentUser = new CurrentUserService(new HttpContextAccessor { HttpContext = httpContext }, _db);
        return new IndexModel(currentUser, new NotificationService(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }
}
