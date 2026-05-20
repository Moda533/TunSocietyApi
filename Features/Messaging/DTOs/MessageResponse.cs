using TunSociety.Api.Models;

namespace TunSociety.Api.DTOs.Message;

public class MessageResponse
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Content { get; set; } = string.Empty;
    public double Score { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }

    public static MessageResponse FromEntity(Models.Message message)
    {
        return new MessageResponse
        {
            Id = message.Id,
            UserId = message.UserId,
            Content = message.Content,
            Score = message.Score,
            Status = message.Status,
            CreatedAtUtc = message.CreatedAtUtc
        };
    }
}
