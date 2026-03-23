using Chat.Identity.Application.Interfaces;
using Microsoft.AspNetCore.Authorization;

namespace Chat.Identity.Infrastructure.Authorization;

/// <summary>
/// Authorization requirement carrying a single permission string.
/// </summary>
public class PermissionRequirement(string permission) : IAuthorizationRequirement
{
    public string Permission { get; } = permission;
}

/// <summary>
/// Handles PermissionRequirement by delegating to IPermissionService.
/// Uses ICurrentUser to read the authenticated user's role from the JWT claim.
/// </summary>
public class PermissionRequirementHandler(IPermissionService permissionService, ICurrentUser currentUser)
    : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (!currentUser.IsAuthenticated)
        {
            context.Fail();
            return;
        }

        var allowed = await permissionService.IsAuthorizedAsync(currentUser.UserId, requirement.Permission);
        if (allowed)
            context.Succeed(requirement);
        else
            context.Fail();
    }
}
