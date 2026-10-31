namespace Platform.Auth.Models;

/// <summary>A stored refresh token. Only its SHA-256 hash is in the database, so the token itself is never here.</summary>
public sealed class RefreshTokenRecord
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public Guid AgencyId { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
}
