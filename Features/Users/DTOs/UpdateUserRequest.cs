namespace TunSociety.Api.DTOs.User;

public class UpdateUserRequest
{
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? NewPassword { get; set; }
    public string? ConfirmPassword { get; set; }
    public string? AvatarUrl { get; set; }
    public string? Role { get; set; }
}
