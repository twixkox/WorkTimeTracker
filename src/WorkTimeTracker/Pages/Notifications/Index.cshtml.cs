using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Notifications;

[Authorize]
public class IndexModel : PageModel
{
    private const int PageSize = 20;
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notifications;

    public IndexModel(CurrentUserService currentUser, NotificationService notifications)
    {
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public List<Notification> Items { get; private set; } = new();
    [BindProperty(SupportsGet = true)] public string Filter { get; set; } = "unread";
    [BindProperty(SupportsGet = true)] public NotificationCategory? Category { get; set; }
    [BindProperty(SupportsGet = true)] public int PageNumber { get; set; } = 1;
    public int TotalCount { get; private set; }
    public int TotalPages { get; private set; }
    public int UnreadCount { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        if (_currentUser.UserId is not { } userId) return Challenge();

        Filter = NormalizeFilter(Filter);
        PageNumber = Math.Max(1, PageNumber);
        UnreadCount = await _notifications.GetUnreadCountAsync(userId);
        TotalCount = await _notifications.GetCountAsync(userId, IsUnreadFilter(Filter), Category);
        TotalPages = Math.Max(1, (int)Math.Ceiling(TotalCount / (double)PageSize));
        PageNumber = Math.Min(PageNumber, TotalPages);
        Items = (await _notifications.GetForUserAsync(userId, PageSize, IsUnreadFilter(Filter), Category,
            (PageNumber - 1) * PageSize)).ToList();
        return Page();
    }

    public async Task<IActionResult> OnPostMarkReadAsync(int id, string? filter,
        NotificationCategory? category, int pageNumber = 1)
    {
        if (_currentUser.UserId is not { } userId) return Challenge();

        await _notifications.MarkReadAsync(userId, id);
        return RedirectToPage(new { filter = NormalizeFilter(filter), category, pageNumber = Math.Max(1, pageNumber) });
    }

    public async Task<IActionResult> OnPostMarkAllReadAsync(string? filter,
        NotificationCategory? category, int pageNumber = 1)
    {
        if (_currentUser.UserId is not { } userId) return Challenge();

        await _notifications.MarkAllReadAsync(userId);
        return RedirectToPage(new { filter = NormalizeFilter(filter), category, pageNumber = Math.Max(1, pageNumber) });
    }

    public static string CategoryName(NotificationCategory category) => category switch
    {
        NotificationCategory.Timesheet => "Табель",
        NotificationCategory.Engineering => "Инженерные задачи",
        _ => "Система",
    };

    private static bool IsUnreadFilter(string? filter) => !string.Equals(filter, "all", StringComparison.OrdinalIgnoreCase);

    private static string NormalizeFilter(string? filter) => IsUnreadFilter(filter) ? "unread" : "all";
}
