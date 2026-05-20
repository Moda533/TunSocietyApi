using TunSociety.Api.DTOs.User;

namespace TunSociety.Api.DTOs.Auth;

public class AuthResponse
{
    public string Token { get; set; } = string.Empty;
    public UserResponse User { get; set; } = new();
}
