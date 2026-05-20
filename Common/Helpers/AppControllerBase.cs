using Microsoft.AspNetCore.Mvc;
using TunSociety.Api.Extensions;
using TunSociety.Api.Models;

namespace TunSociety.Api.Controllers;

public abstract class AppControllerBase : ControllerBase
{
    protected Guid? CurrentUserId => User.GetUserId();
    protected bool CurrentUserIsAdmin => User.IsAdmin();
    protected bool CurrentUserIsModeratorOrAdmin => User.IsModeratorOrAdmin();

    protected ActionResult? EnsureCurrentUserMatches(Guid requestedUserId, bool allowAdmin = false)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        if (requestedUserId != Guid.Empty &&
            requestedUserId != currentUserId &&
            !(allowAdmin && CurrentUserIsAdmin))
        {
            return Forbid();
        }

        return null;
    }

    protected ActionResult? EnsureResourceAccess(Guid ownerUserId, bool allowAdmin = false, bool allowModerator = false)
    {
        if (CurrentUserId is not Guid currentUserId)
        {
            return Unauthorized();
        }

        if (ownerUserId == currentUserId)
        {
            return null;
        }

        if (allowAdmin && CurrentUserIsAdmin)
        {
            return null;
        }

        if (allowModerator && CurrentUserIsModeratorOrAdmin)
        {
            return null;
        }

        return Forbid();
    }

    protected ActionResult? EnsureActiveUser(User user)
    {
        if (!user.IsFrozen)
        {
            return null;
        }

        return StatusCode(StatusCodes.Status423Locked, "Your account is currently frozen.");
    }
}
