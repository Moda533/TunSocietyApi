using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;

namespace TunSociety.Api.Infrastructure.Security;

public sealed class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
{
    private readonly ApplicationDbContext _dbContext;

    public PermissionAuthorizationHandler(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        PermissionRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true)
        {
            return;
        }

        var role = RoleNames.Normalize(context.User.FindFirstValue(ClaimTypes.Role));
        if (role == null)
        {
            return;
        }

        var hasPermission = await _dbContext.RolePermissions
            .AsNoTracking()
            .AnyAsync(rolePermission =>
                rolePermission.Role == role &&
                rolePermission.Permission == requirement.Permission);

        if (hasPermission)
        {
            context.Succeed(requirement);
        }
    }
}
