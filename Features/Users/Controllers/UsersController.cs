using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TunSociety.Api.Configuration;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.User;
using TunSociety.Api.Infrastructure;
using TunSociety.Api.Models;
using TunSociety.Api.Services;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class UsersController : AppControllerBase
{
    private static readonly string[] AllowedRoles = [RoleNames.User, RoleNames.Moderator, RoleNames.Admin];

    private readonly ApplicationDbContext _dbContext;
    private readonly ModerationService _moderationService;
    private readonly SanctionService _sanctionService;
    private readonly AuditService _auditService;
    private readonly AvatarStorageService _avatarStorageService;
    private readonly IAuthorizationService _authorizationService;
    private readonly RolePermissionService _rolePermissionService;
    private readonly AdminAccountOptions _adminAccountOptions;

    public UsersController(
        ApplicationDbContext dbContext,
        ModerationService moderationService,
        SanctionService sanctionService,
        AuditService auditService,
        AvatarStorageService avatarStorageService,
        IAuthorizationService authorizationService,
        RolePermissionService rolePermissionService,
        IOptions<AdminAccountOptions> adminAccountOptions)
    {
        _dbContext = dbContext;
        _moderationService = moderationService;
        _sanctionService = sanctionService;
        _auditService = auditService;
        _avatarStorageService = avatarStorageService;
        _authorizationService = authorizationService;
        _rolePermissionService = rolePermissionService;
        _adminAccountOptions = adminAccountOptions.Value;
    }

    [HttpGet("search")]
    public async Task<ActionResult<IEnumerable<UserLookupResponse>>> Search(
        [FromQuery] string? query,
        [FromQuery] int take = 20,
        CancellationToken cancellationToken = default)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        take = Math.Clamp(take, 1, 50);

        var usersQuery = _dbContext.Users
            .AsNoTracking()
            .Include(user => user.Department)
            .Include(user => user.Badge)
            .Where(user => user.Id != currentUserId);

        if (!string.IsNullOrWhiteSpace(query))
        {
            var trimmedQuery = query.Trim();
            usersQuery = usersQuery.Where(user =>
                user.DisplayName.Contains(trimmedQuery) ||
                user.Email.Contains(trimmedQuery) ||
                user.UserName.Contains(trimmedQuery));
        }

        var users = await usersQuery
            .OrderBy(user => user.DisplayName)
            .ThenBy(user => user.Email)
            .Take(take)
            .ToListAsync(cancellationToken);

        return Ok(users.Select(UserLookupResponse.FromEntity).ToList());
    }

    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> GetCurrent(CancellationToken cancellationToken)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.Department)
            .Include(candidate => candidate.Badge)
            .FirstOrDefaultAsync(candidate => candidate.Id == currentUserId, cancellationToken);

        if (user == null)
        {
            return Unauthorized();
        }

        return Ok(await BuildUserResponseAsync(user, cancellationToken));
    }

    [HttpGet("{id:guid}/lookup")]
    public async Task<ActionResult<UserLookupResponse>> GetLookup(Guid id, CancellationToken cancellationToken)
    {
        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.Department)
            .Include(candidate => candidate.Badge)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);

        if (user == null)
        {
            return NotFound();
        }

        return Ok(UserLookupResponse.FromEntity(user));
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<UserResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        if (CurrentUserId is null)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .Include(candidate => candidate.Department)
            .Include(candidate => candidate.Badge)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user == null)
        {
            return NotFound();
        }

        return Ok(await BuildUserResponseAsync(user, cancellationToken));
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<UserResponse>> Update(Guid id, UpdateUserRequest request, CancellationToken cancellationToken)
    {
        var accessError = await EnsureSelfOrUserEditPermissionAsync(id);
        if (accessError is not null)
        {
            return accessError;
        }

        var user = await _dbContext.Users
            .Include(candidate => candidate.Department)
            .Include(candidate => candidate.Badge)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
        if (user == null)
        {
            return NotFound();
        }

        var updateDisplayName = !string.IsNullOrWhiteSpace(request.DisplayName);
        var updateEmail = !string.IsNullOrWhiteSpace(request.Email);
        var updatePassword = !string.IsNullOrWhiteSpace(request.NewPassword) ||
            !string.IsNullOrWhiteSpace(request.ConfirmPassword);
        var updateAvatar = !string.IsNullOrWhiteSpace(request.AvatarUrl);

        if (updateDisplayName || updateEmail || updatePassword || updateAvatar)
        {
            var frozenError = EnsureActiveUser(user);
            if (frozenError is not null)
            {
                return frozenError;
            }
        }

        if (!string.IsNullOrWhiteSpace(request.DisplayName))
        {
            var displayName = request.DisplayName.Trim();
            var moderation = await _moderationService.EvaluateAsync(Guid.NewGuid(), displayName, "PROFILE", cancellationToken);
            if (moderation.Action != "Allow")
            {
                moderation.ContentType = "UserDisplayName";
                moderation.UserId = user.Id;
                moderation.ContentSnapshot = displayName;
                _dbContext.ModerationResults.Add(moderation);

                var outcome = await _sanctionService.EvaluateAsync(user, moderation, cancellationToken);
                if (outcome.Warning != null)
                {
                    _dbContext.Warnings.Add(outcome.Warning);
                }

                if (outcome.Freeze != null)
                {
                    _dbContext.Freezes.Add(outcome.Freeze);
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await _auditService.LogAsync(
                    "user.displayname.rejected",
                    nameof(User),
                    user.Id.ToString(),
                    $"action={moderation.Action};score={moderation.Score:F3};flags={(moderation.Flags.Count == 0 ? "none" : string.Join(',', moderation.Flags))}",
                    CurrentUserId,
                    cancellationToken);

                var message = BuildDisplayNameRejectionMessage(moderation, outcome);
                return outcome.AccountFrozen
                    ? StatusCode(StatusCodes.Status423Locked, message)
                    : BadRequest(message);
            }

            user.DisplayName = displayName;
        }

        if (updateEmail)
        {
            var email = request.Email!.Trim().ToLowerInvariant();
            if (IsAdminEmail(email))
            {
                return BadRequest("This email is reserved for admin login.");
            }

            var emailExists = await _dbContext.Users.AnyAsync(
                candidate => candidate.Id != user.Id && candidate.Email == email,
                cancellationToken);

            if (emailExists)
            {
                return Conflict("Email already exists.");
            }

            user.Email = email;
            user.UserName = email;
        }

        if (updatePassword)
        {
            var newPassword = request.NewPassword ?? string.Empty;
            var confirmPassword = request.ConfirmPassword ?? string.Empty;

            if (string.IsNullOrWhiteSpace(newPassword) || string.IsNullOrWhiteSpace(confirmPassword))
            {
                return BadRequest("New password and confirm password are required.");
            }

            if (!string.Equals(newPassword, confirmPassword, StringComparison.Ordinal))
            {
                return BadRequest("New password and confirm password must match.");
            }

            if (!IsPasswordComplex(newPassword))
            {
                return BadRequest("Password must be at least 8 characters and include letters, numbers, and special characters.");
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(newPassword);
        }

        if (updateAvatar)
        {
            user.AvatarUrl = request.AvatarUrl!.Trim();
        }

        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            var canEditUsers = await _authorizationService.AuthorizeAsync(User, PermissionNames.UsersEdit);
            if (!canEditUsers.Succeeded)
            {
                return Forbid();
            }

            var normalizedRole = RoleNames.Normalize(request.Role);
            if (normalizedRole == null || !AllowedRoles.Contains(normalizedRole))
            {
                return BadRequest("Role must be User, Moderator, or Admin.");
            }

            user.Role = normalizedRole;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        return Ok(await BuildUserResponseAsync(user, cancellationToken));
    }

    [HttpPost("{id:guid}/avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<UserResponse>> UploadAvatar(
        Guid id,
        [FromForm] UploadAvatarRequest request,
        CancellationToken cancellationToken)
    {
        var accessError = await EnsureSelfOrUserEditPermissionAsync(id);
        if (accessError is not null)
        {
            return accessError;
        }

        var user = await _dbContext.Users
            .Include(candidate => candidate.Department)
            .Include(candidate => candidate.Badge)
            .FirstOrDefaultAsync(candidate => candidate.Id == id, cancellationToken);
        if (user == null)
        {
            return NotFound();
        }

        return await UpdateAvatarAsync(user, request, cancellationToken);
    }

    [HttpPost("me/avatar")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(20 * 1024 * 1024)]
    public async Task<ActionResult<UserResponse>> UploadCurrentAvatar(
        [FromForm] UploadAvatarRequest request,
        CancellationToken cancellationToken)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        var user = await _dbContext.Users
            .Include(candidate => candidate.Department)
            .Include(candidate => candidate.Badge)
            .FirstOrDefaultAsync(candidate => candidate.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound();
        }

        return await UpdateAvatarAsync(user, request, cancellationToken);
    }

    private async Task<ActionResult<UserResponse>> UpdateAvatarAsync(
        User user,
        UploadAvatarRequest request,
        CancellationToken cancellationToken)
    {
        var avatarFile = request.Avatar;
        if (avatarFile == null || avatarFile.Length == 0)
        {
            return BadRequest("Please choose an image file.");
        }

        string? newAvatarUrl = null;

        try
        {
            newAvatarUrl = await _avatarStorageService.SaveAvatarAsync(user.Id, avatarFile, cancellationToken);
            user.AvatarUrl = newAvatarUrl;
            await _dbContext.SaveChangesAsync(cancellationToken);
            return Ok(await BuildUserResponseAsync(user, cancellationToken));
        }
        catch (InvalidOperationException ex)
        {
            if (newAvatarUrl is not null)
            {
                _avatarStorageService.DeleteManagedAvatar(newAvatarUrl);
            }

            return BadRequest(ex.Message);
        }
        catch
        {
            if (newAvatarUrl is not null)
            {
                _avatarStorageService.DeleteManagedAvatar(newAvatarUrl);
            }

            return StatusCode(StatusCodes.Status500InternalServerError, "Unable to save your profile picture.");
        }
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

    private async Task<ActionResult?> EnsureSelfOrUserEditPermissionAsync(Guid targetUserId)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        if (currentUserId == targetUserId)
        {
            return null;
        }

        var canEditUsers = await _authorizationService.AuthorizeAsync(User, PermissionNames.UsersEdit);
        return canEditUsers.Succeeded ? null : Forbid();
    }

    private async Task<UserResponse> BuildUserResponseAsync(User user, CancellationToken cancellationToken)
    {
        var permissions = await _rolePermissionService.GetPermissionsForRoleAsync(user.Role, cancellationToken);
        return UserResponse.FromEntity(user).WithPermissions(permissions);
    }

    private static string BuildDisplayNameRejectionMessage(ModerationResult moderation, SanctionOutcome outcome)
    {
        var baseMessage = $"Display name rejected. {moderation.Reason ?? "This display name is not allowed."}";
        if (moderation.Action != "Block")
        {
            return baseMessage;
        }

        var warningMessage = $"{baseMessage} Warning {outcome.WarningCount} of 3.";
        return outcome.AccountFrozen
            ? $"{warningMessage} Your account is now frozen."
            : warningMessage;
    }
}
