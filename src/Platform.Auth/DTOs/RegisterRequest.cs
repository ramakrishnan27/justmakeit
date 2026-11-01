using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>
/// Creates a user, a new organization of the chosen business model (<c>company</c>, <c>agency</c> or
/// <c>creator</c>) on the trial plan, the founder's membership and a first workspace.
/// </summary>
public sealed record RegisterRequest(
    string? Email,
    string? Password,
    string? FullName,
    string? OrganizationName,
    string? BusinessModel,
    string? WorkspaceName)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.Email(errors, Email);
        FieldRules.NewPassword(errors, Password);
        FieldRules.OptionalName(errors, "fullName", "Full name", FullName);
        FieldRules.RequiredName(errors, "organizationName", "Organization name", OrganizationName);
        FieldRules.RequiredBusinessModel(errors, "businessModel", BusinessModel);
        FieldRules.OptionalName(errors, "workspaceName", "Workspace name", WorkspaceName);
        return errors;
    }
}
