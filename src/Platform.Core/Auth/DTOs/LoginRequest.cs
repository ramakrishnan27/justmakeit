using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <param name="AgencyId">The agency to sign in to. Without it, the user's most recently used membership is used.</param>
public sealed record LoginRequest(string? Email, string? Password, Guid? AgencyId)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.Email(errors, Email);
        FieldRules.ExistingPassword(errors, Password);
        return errors;
    }
}
