using Platform.Helpers.Exceptions;

namespace Platform.Auth.Services;

public static class InvitationErrors
{
    /// <summary>The one answer for a link that never existed, was used, was revoked or ran out, so a guess learns nothing.</summary>
    public static NotFoundException Invalid() =>
        new("This invitation is no longer valid. Ask for a new one.", "invitation_invalid");
}
