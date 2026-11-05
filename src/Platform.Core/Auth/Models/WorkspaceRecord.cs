namespace Platform.Auth.Models;

public sealed class WorkspaceRecord
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
