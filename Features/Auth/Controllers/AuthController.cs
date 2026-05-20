using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TunSociety.Api.Configuration;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Auth;
using TunSociety.Api.DTOs.User;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : AppControllerBase
{
    private const int MaxFailedAttempts = 5;
    private static readonly TimeSpan LockoutDuration = TimeSpan.FromMinutes(10);

    private readonly ApplicationDbContext _dbContext;
    private readonly JwtTokenService _jwtTokenService;
    private readonly AdminAccountOptions _adminAccountOptions;
    private readonly ModerationService _moderationService;
    private readonly RolePermissionService _rolePermissionService;

    public AuthController(
        ApplicationDbContext dbContext,
        JwtTokenService jwtTokenService,
        ModerationService moderationService,
        RolePermissionService rolePermissionService,
        IOptions<AdminAccountOptions> adminAccountOptions)
    {
        _dbContext = dbContext;
        _jwtTokenService = jwtTokenService;
        _moderationService = moderationService;
        _rolePermissionService = rolePermissionService;
        _adminAccountOptions = adminAccountOptions.Value;
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.FullName) ||
            string.IsNullOrWhiteSpace(request.Gender) ||
            string.IsNullOrWhiteSpace(request.Password) ||
            string.IsNullOrWhiteSpace(request.ConfirmPassword))
        {
            return BadRequest("Email, full name, gender, age, password, and confirm password are required.");
        }

        request.Email = request.Email.Trim().ToLowerInvariant();
        request.FullName = request.FullName.Trim();
        request.Gender = request.Gender.Trim();

        if (IsAdminEmail(request.Email))
        {
            return BadRequest("This email is reserved for admin login.");
        }

        if (!UserAvatarHelper.IsValidGender(request.Gender))
        {
            return BadRequest("Gender must be Male or Female.");
        }

        if (request.Age < 15)
        {
            return BadRequest("You must be at least 15 years old to create an account.");
        }

        if (!IsPasswordComplex(request.Password))
        {
            return BadRequest("Password must be at least 8 characters and include letters, numbers, and special characters.");
        }

        if (!string.Equals(request.Password, request.ConfirmPassword, StringComparison.Ordinal))
        {
            return BadRequest("Password and confirm password must match.");
        }

        if (!string.IsNullOrWhiteSpace(request.FullName))
        {
            var moderation = await _moderationService.EvaluateAsync(Guid.NewGuid(), request.FullName, "PROFILE", cancellationToken);
            if (moderation.Action != "Allow")
            {
                return BadRequest($"Full name rejected. {moderation.Reason ?? "This full name is not allowed."}");
            }
        }

        var exists = await _dbContext.Users.AnyAsync(
            user => user.Email == request.Email,
            cancellationToken);

        if (exists)
        {
            return Conflict("User already exists.");
        }

        var defaultBadge = await EnsureDefaultBadgeAsync(cancellationToken);

        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = request.Email,
            UserName = request.Email,
            DisplayName = string.IsNullOrWhiteSpace(request.FullName) ? request.Email : request.FullName,
            Gender = request.Gender,
            Age = request.Age,
            AvatarUrl = UserAvatarHelper.ResolveForGender(request.Gender),
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            Role = RoleNames.User,
            BadgeId = defaultBadge.Id,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildAuthResponseAsync(user, cancellationToken));
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest("Email and password are required.");
        }

        request.Email = request.Email.Trim().ToLowerInvariant();

        if (IsAdminEmail(request.Email))
        {
            var adminUser = await EnsureAdminUserAsync(cancellationToken);
            return await TryLoginUserAsync(adminUser, request.Password, cancellationToken);
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(
            u => u.Email == request.Email,
            cancellationToken);

        if (user == null)
        {
            return Unauthorized();
        }

        await _dbContext.Entry(user).Reference(current => current.Department).LoadAsync(cancellationToken);
        await _dbContext.Entry(user).Reference(current => current.Badge).LoadAsync(cancellationToken);

        return await TryLoginUserAsync(user, request.Password, cancellationToken);
    }

    [AllowAnonymous]
    [HttpPost("oauth/google")]
    public ActionResult OAuthGoogle()
    {
        return StatusCode(501, "Google OAuth is not configured yet.");
    }

    [AllowAnonymous]
    [HttpPost("oauth/github")]
    public ActionResult OAuthGithub()
    {
        return StatusCode(501, "GitHub OAuth is not configured yet.");
    }

    private async Task<User> EnsureAdminUserAsync(CancellationToken cancellationToken)
    {
        var adminUser = await _dbContext.Users
            .Include(user => user.Department)
            .Include(user => user.Badge)
            .FirstOrDefaultAsync(
            user => user.Email == _adminAccountOptions.Email,
            cancellationToken);

        var defaultBadge = await EnsureDefaultBadgeAsync(cancellationToken);

        if (adminUser == null)
        {
            adminUser = new User
            {
                Id = Guid.NewGuid(),
                Email = _adminAccountOptions.Email,
                UserName = _adminAccountOptions.Email,
                DisplayName = string.IsNullOrWhiteSpace(_adminAccountOptions.DisplayName)
                    ? "Administrator"
                    : _adminAccountOptions.DisplayName.Trim(),
                Gender = "Male",
                Age = 18,
                AvatarUrl = UserAvatarHelper.MaleAvatarPath,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(_adminAccountOptions.Password),
                Role = RoleNames.Admin,
                BadgeId = defaultBadge.Id,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.Users.Add(adminUser);
            await _dbContext.SaveChangesAsync(cancellationToken);
            adminUser.Badge = defaultBadge;
            return adminUser;
        }

        if (adminUser.Role != RoleNames.Admin)
        {
            adminUser.Role = RoleNames.Admin;
        }

        if (adminUser.BadgeId == Guid.Empty)
        {
            adminUser.BadgeId = defaultBadge.Id;
            adminUser.Badge = defaultBadge;
        }

        if (!BCrypt.Net.BCrypt.Verify(_adminAccountOptions.Password, adminUser.PasswordHash))
        {
            adminUser.PasswordHash = BCrypt.Net.BCrypt.HashPassword(_adminAccountOptions.Password);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return adminUser;
    }

    private async Task<UserBadge> EnsureDefaultBadgeAsync(CancellationToken cancellationToken)
    {
        var badge = await _dbContext.UserBadges
            .FirstOrDefaultAsync(current => current.Id == ClubMembershipDefaults.MemberBadgeId, cancellationToken);

        if (badge != null)
        {
            badge.Name = ClubMembershipDefaults.MemberBadgeName;
            badge.DepartmentId = null;
            badge.IsArchived = false;
            return badge;
        }

        badge = new UserBadge
        {
            Id = ClubMembershipDefaults.MemberBadgeId,
            Name = ClubMembershipDefaults.MemberBadgeName,
            CreatedAtUtc = DateTime.UtcNow,
            UpdatedAtUtc = DateTime.UtcNow,
            IsArchived = false
        };

        _dbContext.UserBadges.Add(badge);
        return badge;
    }

    private async Task<ActionResult<AuthResponse>> TryLoginUserAsync(User user, string password, CancellationToken cancellationToken)
    {
        if (user.LockoutUntilUtc.HasValue && user.LockoutUntilUtc.Value > DateTime.UtcNow)
        {
            return StatusCode(429, "Too many failed attempts. Try again later.");
        }

        if (!BCrypt.Net.BCrypt.Verify(password, user.PasswordHash))
        {
            user.FailedLoginAttempts += 1;
            if (user.FailedLoginAttempts >= MaxFailedAttempts)
            {
                user.LockoutUntilUtc = DateTime.UtcNow.Add(LockoutDuration);
            }

            await _dbContext.SaveChangesAsync(cancellationToken);
            return Unauthorized();
        }

        var normalizedRole = RoleNames.Normalize(user.Role) ?? RoleNames.User;
        if (!string.Equals(user.Role, normalizedRole, StringComparison.Ordinal))
        {
            user.Role = normalizedRole;
        }

        if (user.BadgeId == Guid.Empty)
        {
            var defaultBadge = await EnsureDefaultBadgeAsync(cancellationToken);
            user.BadgeId = defaultBadge.Id;
            user.Badge = defaultBadge;
        }

        user.FailedLoginAttempts = 0;
        user.LockoutUntilUtc = null;
        await _dbContext.SaveChangesAsync(cancellationToken);

        return Ok(await BuildAuthResponseAsync(user, cancellationToken));
    }

    private async Task<AuthResponse> BuildAuthResponseAsync(User user, CancellationToken cancellationToken)
    {
        var permissions = await _rolePermissionService.GetPermissionsForRoleAsync(user.Role, cancellationToken);

        return new AuthResponse
        {
            Token = _jwtTokenService.CreateToken(user),
            User = UserResponse.FromEntity(user).WithPermissions(permissions)
        };
    }

    private bool IsAdminEmail(string email)
    {
        return !string.IsNullOrWhiteSpace(_adminAccountOptions.Email) &&
            email.Equals(_adminAccountOptions.Email, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsPasswordComplex(string password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < 8)
        {
            return false;
        }

        var hasLetter = password.Any(char.IsLetter);
        var hasDigit = password.Any(char.IsDigit);
        var hasSpecial = password.Any(ch => !char.IsLetterOrDigit(ch));

        return hasLetter && hasDigit && hasSpecial;
    }
}
