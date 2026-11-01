namespace Platform.Auth.Models;

/// <summary>A stored site admin refresh token. Only its SHA-256 hash is in the database.</summary>
public sealed class SiteRefreshTokenRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
}
