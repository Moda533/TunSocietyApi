namespace TunSociety.Api.DTOs.Admin;

public class AdminRolePermissionCatalogResponse
{
    public IReadOnlyList<string> Roles { get; set; } = [];
    public IReadOnlyList<string> Permissions { get; set; } = [];
    public IReadOnlyList<AdminRolePermissionSetResponse> RolePermissions { get; set; } = [];
}

public class AdminRolePermissionSetResponse
{
    public string Role { get; set; } = string.Empty;
    public IReadOnlyList<string> Permissions { get; set; } = [];
}

public class UpdateRolePermissionsRequest
{
    public List<string> Permissions { get; set; } = [];
}
