namespace Platform.Auth.Models;

/// <summary>A row of <c>permissions</c>.</summary>
public sealed class PermissionRecord
{
    public string Key { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsProtected { get; set; }
}

/// <summary>A row of <c>role_permissions</c>: whether one business model and role holds one permission.</summary>
public sealed class GrantRecord
{
    public string BusinessModel { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public string PermissionKey { get; set; } = string.Empty;
    public bool IsGranted { get; set; }
}

/// <summary>The whole catalog and every grant, as the Site admin sees them.</summary>
public sealed record PermissionCatalog(IReadOnlyList<PermissionRecord> Permissions, IReadOnlyList<GrantRecord> Grants);
