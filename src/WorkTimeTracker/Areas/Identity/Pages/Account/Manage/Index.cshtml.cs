using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Models;
using WorkTimeTracker.Services;

namespace WorkTimeTracker.Areas.Identity.Pages.Account.Manage;

public class IndexModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly TelegramLinkService _telegramLinks;

    public IndexModel(UserManager<ApplicationUser> userManager, TelegramLinkService telegramLinks)
    {
        _userManager = userManager;
        _telegramLinks = telegramLinks;
    }

    public ApplicationUser Account { get; private set; } = default!;
    public string? LinkCode { get; private set; }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();
        Account = user;
        return Page();
    }

    public async Task<IActionResult> OnPostGenerateCodeAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();
        Account = user;
        if (user.TelegramUserId is not null)
        {
            TempData["Error"] = "Telegram уже привязан. Сначала отвяжите текущий аккаунт.";
            return RedirectToPage();
        }

        LinkCode = await _telegramLinks.CreateCodeAsync(user.Id, cancellationToken);
        return Page();
    }

    public async Task<IActionResult> OnPostUnlinkAsync(CancellationToken cancellationToken)
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return NotFound();

        await _telegramLinks.UnlinkAsync(user.Id, cancellationToken);
        TempData["Success"] = "Telegram-аккаунт отвязан.";
        return RedirectToPage();
    }
}
