using Microsoft.AspNetCore.Identity;

namespace WorkTimeTracker.Models;

/// <summary>Учётная запись пользователя.</summary>
public class ApplicationUser : IdentityUser
{
    /// <summary>Отображаемое имя (ФИО).</summary>
    public string DisplayName { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
