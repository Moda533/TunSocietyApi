using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Models;

namespace TunSociety.Api.Services;

public sealed class RolePermissionService
{
    private readonly ApplicationDbContext _dbContext;

    public RolePermissionService(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        if (await _dbContext.RolePermissions.AnyAsync(cancellationToken))
        {
            return;
        }

        var now = DateTime.UtcNow;
        foreach (var (role, permissions) in PermissionNames.DefaultPermissionsByRole)
        {
            foreach (var permission in permissions)
            {
                _dbContext.RolePermissions.Add(new RolePermission
                {
                    Id = Guid.NewGuid(),
                    Role = role,
                    Permission = permission,
                    CreatedAtUtc = now,
                    UpdatedAtUtc = now
                });
            }
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetPermissionsForRoleAsync(
        string? role,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = RoleNames.Normalize(role);
        if (normalizedRole == null)
        {
            return [];
        }

        return await _dbContext.RolePermissions
            .AsNoTracking()
            .Where(rolePermission => rolePermission.Role == normalizedRole)
            .OrderBy(rolePermission => rolePermission.Permission)
            .Select(rolePermission => rolePermission.Permission)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetPermissionMatrixAsync(
        CancellationToken cancellationToken = default)
    {
        var records = await _dbContext.RolePermissions
            .AsNoTracking()
            .OrderBy(rolePermission => rolePermission.Role)
            .ThenBy(rolePermission => rolePermission.Permission)
            .ToListAsync(cancellationToken);

        return PermissionNames.SystemRoles.ToDictionary(
            role => role,
            role => (IReadOnlyList<string>)records
                .Where(rolePermission => rolePermission.Role == role)
                .Select(rolePermission => rolePermission.Permission)
                .ToList(),
            StringComparer.Ordinal);
    }

    public async Task<IReadOnlyList<string>> ReplacePermissionsForRoleAsync(
        string role,
        IEnumerable<string> permissions,
        CancellationToken cancellationToken = default)
    {
        var normalizedRole = RoleNames.Normalize(role)
            ?? throw new ArgumentException("Unknown role.", nameof(role));

        var requestedPermissions = permissions
            .Where(permission => !string.IsNullOrWhiteSpace(permission))
            .Select(permission => permission.Trim())
            .Distinct(StringComparer.Ordinal)
            .OrderBy(permission => permission)
            .ToList();

        var existing = await _dbContext.RolePermissions
            .Where(rolePermission => rolePermission.Role == normalizedRole)
            .ToListAsync(cancellationToken);

        _dbContext.RolePermissions.RemoveRange(existing);

        var now = DateTime.UtcNow;
        foreach (var permission in requestedPermissions)
        {
            _dbContext.RolePermissions.Add(new RolePermission
            {
                Id = Guid.NewGuid(),
                Role = normalizedRole,
                Permission = permission,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            });
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return requestedPermissions;
    }
}
