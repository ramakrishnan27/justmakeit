using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>One user's membership of one agency, with the agency name for display.</summary>
public sealed class MembershipRecord
{
    public Guid AgencyId { get; set; }
    public string AgencyName { get; set; } = string.Empty;
    public AgencyRole Role { get; set; }
}
