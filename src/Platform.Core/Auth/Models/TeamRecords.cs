using Platform.Helpers.Constants;

namespace Platform.Auth.Models;

/// <summary>One member of the organization's team, as <c>team_members_v2()</c> returns them.</summary>
public sealed class TeamMemberRecord
{
    public Guid UserId { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public OrganizationRole Role { get; set; }
    public DateTimeOffset JoinedAt { get; set; }

    /// <summary>False when the person has been deactivated: they keep their place in the team but cannot sign in.</summary>
    public bool IsActive { get; set; } = true;
}

/// <summary>An invitation that has not been accepted or revoked yet. It may have expired.</summary>
public sealed class InvitationRecord
{
    public Guid Id { get; set; }

    /// <summary>The name the admin typed. Empty for an invitation made before names were asked for.</summary>
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public OrganizationRole Role { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>An invitation to write. Only the hash of the token is stored; the token itself is shown once.</summary>
public sealed record NewInvitation(
    Guid Id,
    Guid AgencyId,
    BusinessModel BusinessModel,
    string FullName,
    string Email,
    OrganizationRole Role,
    string TokenHash,
    Guid InvitedBy,
    DateTimeOffset CreatedAt,
    DateTimeOffset ExpiresAt);

/// <summary>
/// What the invitation page may know about a pending invitation, found by the hash of its token before anybody is
/// signed in: where it leads, as whom, and for which address.
/// </summary>
public sealed class InvitationPreviewRecord
{
    public Guid Id { get; set; }
    public Guid AgencyId { get; set; }
    public string OrganizationName { get; set; } = string.Empty;
    public BusinessModel BusinessModel { get; set; }
    public string Email { get; set; } = string.Empty;
    public OrganizationRole Role { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }

    /// <summary>The name the admin typed, to offer on the page. Empty for an older invitation.</summary>
    public string FullName { get; set; } = string.Empty;
}

/// <summary>Where an accepted invitation put the new member.</summary>
public sealed class AcceptedInvitationRecord
{
    public Guid AgencyId { get; set; }
    public OrganizationRole Role { get; set; }
}

/// <summary>One ended session: access tokens of this user for this organization issued up to <see cref="ValidAfter"/> are void.</summary>
public sealed class SessionRevocationRecord
{
    public Guid AgencyId { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ValidAfter { get; set; }
}

/// <summary>
/// Every recent ended session, keyed by organization and user, as whole seconds since 1970 (the resolution of a token's
/// <c>iat</c>). A token is void when it was issued at or before that second, which also refuses one issued in the
/// same second after the change: the person signs in again, which is harmless and keeps the rule closed.
/// </summary>
public sealed class SessionRevocationSnapshot(IReadOnlyDictionary<(Guid AgencyId, Guid UserId), long> validAfterSeconds)
{
    public static SessionRevocationSnapshot Empty { get; } = new(new Dictionary<(Guid, Guid), long>());

    public bool IsRevoked(Guid agencyId, Guid userId, long issuedAtSeconds) =>
        validAfterSeconds.TryGetValue((agencyId, userId), out var after) && issuedAtSeconds <= after;

    public static SessionRevocationSnapshot From(IEnumerable<SessionRevocationRecord> rows) =>
        new(rows.ToDictionary(row => (row.AgencyId, row.UserId), row => row.ValidAfter.ToUnixTimeSeconds()));
}
