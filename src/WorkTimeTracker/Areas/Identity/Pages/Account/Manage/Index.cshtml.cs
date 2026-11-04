using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WorkTimeTracker.Areas.Identity.Pages.Account.Manage;

public class IndexModel : PageModel
{
    public IActionResult OnGet() => RedirectToPage("./ChangePassword");
}
