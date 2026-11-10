using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>One row of site_admin_list_agencies_v3(). <see cref="TotalCount"/> repeats on every row.</summary>
public sealed class SiteAgencyRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public BusinessModel BusinessModel { get; set; }
    public AgencyPlan Plan { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public long MemberCount { get; set; }
    public long WorkspaceCount { get; set; }

    /// <summary>False when the Site admin has deactivated the organization.</summary>
    public bool IsActive { get; set; } = true;

    /// <summary>How many channels are enabled for the organization.</summary>
    public long ChannelCount { get; set; }

    public long TotalCount { get; set; }
}
