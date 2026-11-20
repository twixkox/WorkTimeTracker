using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Pages.Notifications;

[Authorize]
public class SettingsModel : PageModel
{
    private readonly CurrentUserService _currentUser;
    private readonly NotificationPreferenceService _preferences;

    public SettingsModel(CurrentUserService currentUser, NotificationPreferenceService preferences)
    {
        _currentUser = currentUser;
        _preferences = preferences;
    }

    [BindProperty] public NotificationPreferences Preferences { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (_currentUser.UserId is not { } userId) return Challenge();

        Preferences = await _preferences.GetAsync(userId);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (_currentUser.UserId is not { } userId) return Challenge();
        if (!ModelState.IsValid) return Page();

        await _preferences.SaveAsync(userId, Preferences);
        TempData["Success"] = "Настройки уведомлений сохранены.";
        return RedirectToPage();
    }
}
