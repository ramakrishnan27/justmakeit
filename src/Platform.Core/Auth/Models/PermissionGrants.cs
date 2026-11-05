namespace Platform.Auth.Models;

/// <summary>
/// What an access token carries for a member: the permission keys the member's role holds and the
/// permission version those keys were read under. A token whose version is older than the current one
/// is refused (401), so a change the Site admin makes reaches a signed-in person when they next sign in.
/// </summary>
public sealed record PermissionGrants(long Version, IReadOnlyList<string> Keys);
