using System.Collections.Frozen;
using Platform.Helpers.Exceptions;

namespace Platform.Helpers.Tenancy;

/// <summary>
/// Mutable holder registered per scope. The tenant middleware fills it from the access token;
/// background workers fill it with the agency they are processing.
/// </summary>
public sealed class TenantContext : ITenantContext
{
    public Guid? AgencyId { get; private set; }
    public Guid? UserId { get; private set; }
    public string? Role { get; private set; }
    public IReadOnlySet<string> Permissions { get; private set; } = FrozenSet<string>.Empty;

    public void Set(Guid? agencyId, Guid? userId, string? role = null, IEnumerable<string>? permissions = null)
    {
        AgencyId = agencyId;
        UserId = userId;
        Role = role;
        Permissions = permissions is null ? FrozenSet<string>.Empty : permissions.ToFrozenSet(StringComparer.Ordinal);
    }

    public void Clear() => Set(null, null);

    public bool HasPermission(string permission) => Permissions.Contains(permission);

    public Guid RequireAgencyId() =>
        AgencyId ?? throw new UnauthorizedException("No agency is selected for this request.");

    public Guid RequireUserId() =>
        UserId ?? throw new UnauthorizedException("No user is signed in for this request.");
}
