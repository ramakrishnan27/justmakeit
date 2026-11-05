using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

public sealed record CreateWorkspaceRequest(string? Name)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        FieldRules.RequiredName(errors, "name", "Name", Name);
        return errors;
    }
}
