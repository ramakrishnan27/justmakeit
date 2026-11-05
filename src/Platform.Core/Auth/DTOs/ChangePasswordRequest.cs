using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>
/// A signed-in person changes their own password. The current one is asked for again, so a session left open on a shared
/// computer cannot be turned into a new password. Both travel in the body only and are never logged.
/// </summary>
public sealed record ChangePasswordRequest(string? CurrentPassword, string? NewPassword)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.ExistingPassword(errors, CurrentPassword, "currentPassword");
        FieldRules.NewPassword(errors, NewPassword, "newPassword");
        return errors;
    }
}
