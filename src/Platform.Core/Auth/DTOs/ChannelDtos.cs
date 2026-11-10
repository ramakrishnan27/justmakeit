using Platform.Helpers.Validation;

namespace Platform.Auth.DTOs;

/// <summary>A channel as the Site admin's list shows it.</summary>
public sealed record SiteChannelResponse(
    Guid Id,
    string Name,
    string Description,
    string? IconKey,
    bool HasIconImage,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    long OrganizationCount);

/// <summary>A channel and whether it is enabled for one organization.</summary>
public sealed record OrganizationChannelResponse(
    Guid Id,
    string Name,
    string Description,
    string? IconKey,
    bool HasIconImage,
    bool IsEnabled);

/// <summary>Enables or disables one channel for one organization.</summary>
public sealed record SetOrganizationChannelRequest(bool? Enabled)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (Enabled is null)
        {
            errors.Add("enabled", "Enabled must be true or false.");
        }

        return errors;
    }
}

/// <summary>Deactivates or activates an organization.</summary>
public sealed record SetOrganizationActiveRequest(bool? IsActive)
{
    public ValidationErrors Validate()
    {
        var errors = new ValidationErrors();
        if (IsActive is null)
        {
            errors.Add("isActive", "isActive must be true or false.");
        }

        return errors;
    }
}
