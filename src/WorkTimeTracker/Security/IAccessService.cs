namespace WorkTimeTracker.Security;

public interface IAccessService
{
    Task<IReadOnlyCollection<string>> GetPermissionsAsync(string userId,
        CancellationToken cancellationToken = default);

    Task<bool> HasPermissionAsync(string userId, string permission,
        CancellationToken cancellationToken = default);

    Task<bool> HasAccessAsync(string userId, string permission, string? ownerUserId = null,
        int? departmentId = null, CancellationToken cancellationToken = default);
}
