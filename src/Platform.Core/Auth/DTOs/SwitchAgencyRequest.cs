using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

public sealed record SwitchAgencyRequest(Guid? AgencyId)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (AgencyId is null || AgencyId == Guid.Empty)
        {
            errors.Add("agencyId", "Agency is required.");
        }

        return errors;
    }
}
