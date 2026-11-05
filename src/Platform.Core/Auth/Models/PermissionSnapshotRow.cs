namespace Platform.Auth.Models;

/// <summary>One row of <c>permission_snapshot()</c>. The grant columns are null when there are no grants at all.</summary>
public sealed class PermissionSnapshotRow
{
    public long Version { get; set; }
    public string? BusinessModel { get; set; }
    public string? Role { get; set; }
    public string? PermissionKey { get; set; }
    public bool? IsGranted { get; set; }
}
