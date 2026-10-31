namespace Platform.Auth.Models;

public sealed record AccessToken(string Value, DateTimeOffset ExpiresAt);
