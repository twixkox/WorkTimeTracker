using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Areas.Identity.Pages.Account;

[Authorize(Roles = AppRoles.Admin)]
public class RegisterModel : PageModel
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;

    public RegisterModel(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole> roleManager)
    {
        _userManager = userManager;
        _roleManager = roleManager;
    }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public class InputModel
    {
        [Required(ErrorMessage = "Укажите ФИО")]
        [Display(Name = "ФИО")]
        public string DisplayName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите email")]
        [EmailAddress(ErrorMessage = "Некорректный адрес email")]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Укажите пароль")]
        [StringLength(100, ErrorMessage = "Пароль должен быть не короче {2} символов.", MinimumLength = 6)]
        [DataType(DataType.Password)]
        [Display(Name = "Пароль")]
        public string Password { get; set; } = string.Empty;

        [DataType(DataType.Password)]
        [Display(Name = "Подтверждение пароля")]
        [Compare("Password", ErrorMessage = "Пароли не совпадают.")]
        public string ConfirmPassword { get; set; } = string.Empty;

        [Required(ErrorMessage = "Выберите роль")]
        [Display(Name = "Роль")]
        public string Role { get; set; } = AppRoles.Employee;
    }

    public void OnGet() { }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid) return Page();

        var user = new ApplicationUser
        {
            UserName = Input.Email,
            Email = Input.Email,
            EmailConfirmed = true,
            DisplayName = Input.DisplayName,
            IsActive = true,
        };
        var result = await _userManager.CreateAsync(user, Input.Password);
        if (result.Succeeded)
        {
            if (!await _roleManager.RoleExistsAsync(Input.Role))
                await _roleManager.CreateAsync(new IdentityRole(Input.Role));
            await _userManager.AddToRoleAsync(user, Input.Role);
            TempData["Success"] = $"Пользователь {Input.Email} создан с ролью «{RoleName.ToRussian(Input.Role)}». Теперь привяжите его к карточке сотрудника на странице «Пользователи и роли».";
            return RedirectToPage("/Admin/Users", new { area = "" });
        }

        foreach (var error in result.Errors)
            ModelState.AddModelError(string.Empty, error.Description);
        return Page();
    }
}
