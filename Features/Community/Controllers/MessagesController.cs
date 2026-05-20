using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TunSociety.Api.Data;
using TunSociety.Api.DTOs.Common;
using TunSociety.Api.DTOs.Moderation;
using TunSociety.Api.DTOs.Message;
using TunSociety.Api.Services;
using TunSociety.Api.Models;

namespace TunSociety.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class MessagesController : AppControllerBase
{
    private readonly ApplicationDbContext _dbContext;
    private readonly ModerationService _moderationService;
    private readonly SanctionService _sanctionService;
    private readonly AuditService _auditService;

    public MessagesController(
        ApplicationDbContext dbContext,
        ModerationService moderationService,
        SanctionService sanctionService,
        AuditService auditService)
    {
        _dbContext = dbContext;
        _moderationService = moderationService;
        _sanctionService = sanctionService;
        _auditService = auditService;
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<MessageResponse>> GetById(Guid id, CancellationToken cancellationToken)
    {
        var message = await _dbContext.Messages.AsNoTracking().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);
        if (message == null)
        {
            return NotFound();
        }

        var accessError = EnsureResourceAccess(message.UserId, allowModerator: true);
        if (accessError is not null)
        {
            return accessError;
        }

        return Ok(MessageResponse.FromEntity(message));
    }

    [HttpPost]
    public async Task<ActionResult<SubmissionResult<MessageResponse>>> Create(CreateMessageRequest request, CancellationToken cancellationToken)
    {
        var accessError = EnsureCurrentUserMatches(request.UserId);
        if (accessError is not null)
        {
            return accessError;
        }

        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return BadRequest("Message content is required.");
        }

        var currentUserId = CurrentUserId!.Value;
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == currentUserId, cancellationToken);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var frozenError = EnsureActiveUser(user);
        if (frozenError is not null)
        {
            return frozenError;
        }

        var messageId = Guid.NewGuid();
        var rawContent = request.Content.Trim();
        var moderation = await _moderationService.EvaluateAsync(messageId, rawContent, "MESSAGE", cancellationToken);

        moderation.ContentType = nameof(Message);
        moderation.UserId = user.Id;
        moderation.ContentSnapshot = rawContent;
        _dbContext.ModerationResults.Add(moderation);

        Message? message = null;
        if (moderation.Action == "Allow")
        {
            message = new Message
            {
                Id = messageId,
                UserId = user.Id,
                Content = rawContent,
                Score = moderation.Score,
                Status = moderation.Action,
                CreatedAtUtc = DateTime.UtcNow
            };

            _dbContext.Messages.Add(message);
        }

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
            "message.create",
            nameof(Message),
            message?.Id.ToString() ?? messageId.ToString(),
            $"action={moderation.Action};score={moderation.Score:F3};flags={(moderation.Flags.Count == 0 ? "none" : string.Join(',', moderation.Flags))}",
            user.Id,
            cancellationToken);

        return Ok(new SubmissionResult<MessageResponse>
        {
            Data = message == null ? null : MessageResponse.FromEntity(message),
            Moderation = ModerationFeedbackResponse.From(moderation, outcome)
        });
    }
}
