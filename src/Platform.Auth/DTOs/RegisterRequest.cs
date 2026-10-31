using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>Creates a user, a new agency on the trial plan, an owner membership and a first workspace.</summary>
public sealed record RegisterRequest(
    string? Email,
    string? Password,
    string? FullName,
    string? AgencyName,
    string? WorkspaceName)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.Email(errors, Email);
        FieldRules.NewPassword(errors, Password);
        FieldRules.OptionalName(errors, "fullName", "Full name", FullName);
        FieldRules.RequiredName(errors, "agencyName", "Agency name", AgencyName);
        FieldRules.OptionalName(errors, "workspaceName", "Workspace name", WorkspaceName);
        return errors;
    }
}
