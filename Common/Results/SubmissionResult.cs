using TunSociety.Api.DTOs.Moderation;

namespace TunSociety.Api.DTOs.Common;

public class SubmissionResult<T>
{
    public T? Data { get; set; }
    public ModerationFeedbackResponse Moderation { get; set; } = new();
}
