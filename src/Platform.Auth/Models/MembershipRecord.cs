using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>One user's membership of one organization, with its name and business model for display.</summary>
public sealed class MembershipRecord
{
    public Guid AgencyId { get; set; }
    public string AgencyName { get; set; } = string.Empty;
    public BusinessModel BusinessModel { get; set; }
    public OrganizationRole Role { get; set; }
}
