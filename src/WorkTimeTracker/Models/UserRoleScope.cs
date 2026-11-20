using Microsoft.AspNetCore.Identity;

namespace WorkTimeTracker.Models;

public enum AccessScope
{
    Own = 0,
    Department = 1,
    Global = 2,
}

/// <summary>Область действия конкретной роли пользователя.</summary>
public class UserRoleScope
{
    public int Id { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string RoleId { get; set; } = string.Empty;
    public AccessScope Scope { get; set; }
    public int? DepartmentId { get; set; }
    public DateTime? ValidFrom { get; set; }
    public DateTime? ValidTo { get; set; }

    public ApplicationUser? User { get; set; }
    public IdentityRole? Role { get; set; }
    public Department? Department { get; set; }
}
