using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>
/// The query of <c>GET /v1/auth/google/start</c>. <c>intent=login</c> signs in an existing Google user.
/// <c>intent=register</c> also needs the business model and organization name the person chose, because
/// Google only supplies who they are.
/// </summary>
public sealed record GoogleStartRequest(string? Intent, string? BusinessModel, string? OrganizationName)
{
    public const string Login = "login";
    public const string Register = "register";

    public bool IsRegistration => Intent == Register;

    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (Intent is not (Login or Register))
        {
            errors.Add("intent", "Intent must be login or register.");
            return errors;
        }

        if (IsRegistration)
        {
            FieldRules.RequiredName(errors, "organizationName", "Organization name", OrganizationName);
            FieldRules.RequiredBusinessModel(errors, "businessModel", BusinessModel);
        }

        return errors;
    }
}

/// <summary>What the pages ask first: which sign-in methods this server offers.</summary>
public sealed record ProvidersResponse(bool Google);
