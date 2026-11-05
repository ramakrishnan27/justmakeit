using Platform.Auth.DTOs;
using Platform.Helpers.Constants;
using Platform.Helpers.Validation;

namespace Platform.Auth.Models;

/// <summary>
/// A registration whose person was identified by an outside provider: their email and name are the
/// provider's, there is no password, and the organization comes from the form they filled in first.
/// </summary>
public sealed record ExternalRegistration(
    string Provider,
    string Subject,
    string Email,
    string? FullName,
    string? OrganizationName,
    BusinessModel BusinessModel)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.Email(errors, Email);
        FieldRules.OptionalName(errors, "fullName", "Full name", FullName);
        FieldRules.RequiredName(errors, "organizationName", "Organization name", OrganizationName);
        if (string.IsNullOrWhiteSpace(Subject))
        {
            errors.Add("subject", "The provider did not identify the person.");
        }

        return errors;
    }
}
