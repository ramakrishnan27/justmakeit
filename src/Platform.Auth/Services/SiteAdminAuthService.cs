using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Settings;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Utilities;

namespace Platform.Auth.Services;

/// <summary>
/// Sign-in and sessions for site admins. It mirrors <see cref="AuthService"/>: PBKDF2 passwords, a
/// rotating refresh token with reuse detection, only a hash stored. The differences are that there is
/// no agency, and that everything first asks whether the user is (still) in site_admins.
/// </summary>
public sealed class SiteAdminAuthService(
    IAccountRepository accounts,
    ISiteAdminRepository siteAdmins,
    ISiteAdminRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokens,
    ITenantContext tenant,
    IOptions<JwtSettings> jwtOptions,
    TimeProvider time,
    ILogger<SiteAdminAuthService> logger) : ISiteAdminAuthService
{
    // Longer than any token we hand out; anything bigger is junk and is not worth hashing.
    private const int MaxRefreshTokenLength = 256;

    public async Task<AuthResult> LoginAsync(SiteLoginRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();

        var user = await accounts.FindLoginByEmailAsync(EmailAddressHelper.Normalize(request.Email), cancellationToken);

        // Always verify something, so an unknown email costs the same time as a wrong password.
        var passwordMatches = passwordHasher.Verify(request.Password!, user?.PasswordHash ?? passwordHasher.DummyHash);
        if (user is null || !passwordMatches || !await siteAdmins.IsSiteAdminAsync(user.Id, cancellationToken))
        {
            // One error for "no such user", "wrong password" and "not a site admin": the endpoint must not
            // tell a visitor which addresses belong to site admins.
            throw new UnauthorizedException("Email or password is incorrect.", "invalid_credentials");
        }

        logger.LogInformation("Site admin {UserId} signed in", user.Id);
        return await StartSessionAsync(user.Id, cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        var presented = await FindRefreshTokenAsync(refreshToken, cancellationToken) ?? throw InvalidRefreshToken();
        var now = time.GetUtcNow();

        if (presented.RevokedAt is not null)
        {
            // A token that was already rotated or revoked is being used again: it was copied.
            await RevokeAllAfterReuseAsync(presented, now, cancellationToken);
            throw InvalidRefreshToken();
        }

        if (presented.ExpiresAt <= now)
        {
            throw InvalidRefreshToken();
        }

        // Losing the site admin role ends the session at the next refresh at the latest.
        if (!await siteAdmins.IsSiteAdminAsync(presented.UserId, cancellationToken))
        {
            await refreshTokens.RevokeAllAsync(presented.UserId, now, cancellationToken);
            throw InvalidRefreshToken();
        }

        var (rawToken, replacement) = NewRefreshToken(presented.UserId, now);
        if (!await refreshTokens.RotateAsync(presented.Id, replacement, now, cancellationToken))
        {
            // Someone else revoked this token between our check and the rotation: same as reuse.
            await RevokeAllAfterReuseAsync(presented, now, cancellationToken);
            throw InvalidRefreshToken();
        }

        return BuildResult(presented.UserId, rawToken, replacement.ExpiresAt);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        var presented = await FindRefreshTokenAsync(refreshToken, cancellationToken);
        if (presented is null)
        {
            return;
        }

        await refreshTokens.RevokeAsync(presented.UserId, presented.Id, time.GetUtcNow(), cancellationToken);
    }

    public async Task<SiteMeResponse> GetMeAsync(CancellationToken cancellationToken)
    {
        var userId = tenant.RequireUserId();

        // The token outlives a revocation by up to its lifetime; the database has the final say.
        if (tenant.Role != RoleNames.SiteAdmin || !await siteAdmins.IsSiteAdminAsync(userId, cancellationToken))
        {
            throw new UnauthorizedException("You are no longer a site admin.", "not_site_admin");
        }

        var user = await accounts.GetUserAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedException("The account no longer exists.", "unknown_user");

        return new SiteMeResponse(new UserDto(user.Id, user.Email, user.FullName), RoleNames.SiteAdmin);
    }

    private async Task<AuthResult> StartSessionAsync(Guid userId, CancellationToken cancellationToken)
    {
        var (rawToken, token) = NewRefreshToken(userId, time.GetUtcNow());
        await refreshTokens.InsertAsync(token, cancellationToken);
        return BuildResult(userId, rawToken, token.ExpiresAt);
    }

    private (string Raw, NewSiteRefreshToken Stored) NewRefreshToken(Guid userId, DateTimeOffset now)
    {
        // Only the hash is stored. The raw value exists here and in the cookie we send, nowhere else.
        var raw = TokenGenerator.Create(TokenGenerator.DefaultByteLength);
        var stored = new NewSiteRefreshToken(
            Guid.NewGuid(), userId, HashHelper.Sha256Hex(raw), now.AddDays(jwtOptions.Value.RefreshTokenDays));
        return (raw, stored);
    }

    private AuthResult BuildResult(Guid userId, string rawRefreshToken, DateTimeOffset refreshExpiresAt)
    {
        var access = accessTokens.IssueSiteAdmin(userId);
        var expiresIn = jwtOptions.Value.AccessTokenMinutes * 60;
        return new AuthResult(new TokenResponse(access.Value, TokenResponse.BearerType, expiresIn), rawRefreshToken, refreshExpiresAt);
    }

    private async Task<SiteRefreshTokenRecord?> FindRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > MaxRefreshTokenLength)
        {
            return null;
        }

        return await refreshTokens.FindByHashAsync(HashHelper.Sha256Hex(refreshToken), cancellationToken);
    }

    private async Task RevokeAllAfterReuseAsync(SiteRefreshTokenRecord presented, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var revoked = await refreshTokens.RevokeAllAsync(presented.UserId, now, cancellationToken);
        logger.LogWarning(
            "A revoked site admin refresh token was presented again for user {UserId}; revoked {Count} live tokens",
            presented.UserId, revoked);
    }

    private static UnauthorizedException InvalidRefreshToken() =>
        new("Your session has expired. Sign in again.", "invalid_refresh_token");
}
