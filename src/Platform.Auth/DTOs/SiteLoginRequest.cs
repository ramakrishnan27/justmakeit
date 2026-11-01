using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

public sealed record SiteLoginRequest(string? Email, string? Password)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.Email(errors, Email);
        FieldRules.ExistingPassword(errors, Password);
        return errors;
    }
}
