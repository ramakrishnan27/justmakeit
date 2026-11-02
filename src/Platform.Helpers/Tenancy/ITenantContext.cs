namespace Platform.Helpers.Tenancy;

/// <summary>
/// The tenant the current unit of work acts for. The connection factory copies these values into
/// the database session so row-level security policies can see them.
/// </summary>
public interface ITenantContext
{
    Guid? AgencyId { get; }
    Guid? UserId { get; }
    string? Role { get; }

    /// <summary>
    /// The permission keys the access token carried (see <c>PermissionKeys</c>). Empty when there is no
    /// token, for a site admin, and for background work, so a missing permission is always a denial.
    /// </summary>
    IReadOnlySet<string> Permissions { get; }

    /// <summary>Whether the caller's token carried <paramref name="permission"/>. Services use it to re-check what the policy already checked.</summary>
    bool HasPermission(string permission);

    /// <summary>Returns the agency id, or throws <see cref="Exceptions.UnauthorizedException"/> when none is set.</summary>
    Guid RequireAgencyId();

    /// <summary>Returns the user id, or throws <see cref="Exceptions.UnauthorizedException"/> when none is set.</summary>
    Guid RequireUserId();
}
