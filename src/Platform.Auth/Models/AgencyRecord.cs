namespace Platform.Auth.Models;

public sealed class AgencyRecord
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public AgencyPlan Plan { get; set; }
}
