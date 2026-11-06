using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace WorkTimeTracker.Pages;

[Authorize]
public class IndexModel : PageModel
{
    public IActionResult OnGet()
    {
        if (User.IsInRole(AppRoles.Admin))
            return RedirectToPage("/Admin/Index");
        if (User.IsInRole(AppRoles.ProductionHead))
            return RedirectToPage("/Production/Dashboard");
        if (User.IsInRole(AppRoles.SectionMaster))
            return RedirectToPage("/Master/Index");
        if (User.IsInRole(AppRoles.Employee))
            return RedirectToPage("/Employee/Index");
        return RedirectToPage("/AccessDenied");
    }
}
