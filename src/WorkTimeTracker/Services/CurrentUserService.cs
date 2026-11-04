using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using WorkTimeTracker.Data;
using WorkTimeTracker.Models;

namespace WorkTimeTracker.Services;

/// <summary>Текущий пользователь и связанный с ним сотрудник.</summary>
public class CurrentUserService
{
    private readonly IHttpContextAccessor _http;
    private readonly ApplicationDbContext _db;
    private Employee? _employee;

    public CurrentUserService(IHttpContextAccessor http, ApplicationDbContext db)
    {
        _http = http;
        _db = db;
    }

    public ClaimsPrincipal? User => _http.HttpContext?.User;

    public string? UserId => User?.FindFirstValue(ClaimTypes.NameIdentifier);

    public string UserName => AuditService.UserName(User ?? new ClaimsPrincipal());

    public bool IsInRole(string role) => User?.IsInRole(role) == true;

    /// <summary>Карточка сотрудника, привязанная к текущей учётной записи (null для непривязанных).</summary>
    public async Task<Employee?> GetEmployeeAsync()
    {
        if (_employee is not null) return _employee;
        var userId = UserId;
        if (userId is null) return null;
        _employee = await _db.Employees
            .Include(e => e.Department)
            .FirstOrDefaultAsync(e => e.UserId == userId && e.IsActive);
        return _employee;
    }
}
