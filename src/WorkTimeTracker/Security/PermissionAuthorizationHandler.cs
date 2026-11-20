using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace WorkTimeTracker.Security;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly IAccessService _access;

    public PermissionAuthorizationHandler(IAccessService access) => _access = access;

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        var userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId is not null && await _access.HasPermissionAsync(userId, requirement.Permission))
            context.Succeed(requirement);
    }
}
