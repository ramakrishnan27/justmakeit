using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>Grants or revokes one permission for one business model and role. The model and role are the stored lower-case names.</summary>
public sealed record SetGrantRequest(string? BusinessModel, string? Role, bool? Granted)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.RequiredBusinessModel(errors, "businessModel", BusinessModel);
        FieldRules.RequiredRole(errors, "role", Role);
        if (Granted is null)
        {
            errors.Add("granted", "Granted must be true or false.");
        }

        return errors;
    }
}
