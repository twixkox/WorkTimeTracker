using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Notifications;

[Authorize]
public class IndexModel : PageModel
{
    private readonly CurrentUserService _currentUser;
    private readonly NotificationService _notifications;

    public IndexModel(CurrentUserService currentUser, NotificationService notifications)
    {
        _currentUser = currentUser;
        _notifications = notifications;
    }

    public List<Notification> Items { get; private set; } = new();

    public async Task<IActionResult> OnGet()
    {
        if (_currentUser.UserId is null) return Challenge();
        Items = (await _notifications.GetForUserAsync(_currentUser.UserId)).ToList();
        await _notifications.MarkAllReadAsync(_currentUser.UserId);
        return Page();
    }
}
