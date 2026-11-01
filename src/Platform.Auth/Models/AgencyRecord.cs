using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>An organization (called an agency in the code and the database).</summary>
public sealed class AgencyRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public BusinessModel BusinessModel { get; set; }
    public AgencyPlan Plan { get; set; }
}
