using Microsoft.Extensions.Logging;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Utilities;

namespace Platform.Auth.Services;

/// <summary>
/// Sign-in for site admins. It mirrors <see cref="AuthService"/>: PBKDF2 passwords and a signed access token that is the
/// whole session (nothing is stored). The differences are that there is no agency, and that every data request
/// first asks whether the user is (still) in site_admins, so a demoted admin is cut off at once even though the
/// token they hold stays valid until it expires.
/// </summary>
public sealed class SiteAdminAuthService(
    IAccountRepository accounts,
    ISiteAdminRepository siteAdmins,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokens,
    ITenantContext tenant,
    ILogger<SiteAdminAuthService> logger) : ISiteAdminAuthService
{
    public async Task<AuthResult> LoginAsync(SiteLoginRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();

        var user = await accounts.FindLoginByEmailAsync(EmailAddressHelper.Normalize(request.Email), cancellationToken);

        // Always verify something, so an unknown email costs the same time as a wrong password.
        var storedHash = user?.PasswordHash;
        var passwordMatches = passwordHasher.Verify(request.Password!, storedHash ?? passwordHasher.DummyHash);
        if (user is null || storedHash is null || !passwordMatches || !await siteAdmins.IsSiteAdminAsync(user.Id, cancellationToken))
        {
            // One error for "no such user", "wrong password" and "not a site admin": the endpoint must not
            // tell a visitor which addresses belong to site admins.
            throw new UnauthorizedException("Email or password is incorrect.", "invalid_credentials");
        }

        logger.LogInformation("Site admin {UserId} signed in", user.Id);
        var access = accessTokens.IssueSiteAdmin(user.Id);
        return new AuthResult(access.Value, access.ExpiresAt);
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
}
