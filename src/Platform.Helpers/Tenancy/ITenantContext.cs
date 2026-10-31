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

    /// <summary>Returns the agency id, or throws <see cref="Exceptions.UnauthorizedException"/> when none is set.</summary>
    Guid RequireAgencyId();

    /// <summary>Returns the user id, or throws <see cref="Exceptions.UnauthorizedException"/> when none is set.</summary>
    Guid RequireUserId();
}
