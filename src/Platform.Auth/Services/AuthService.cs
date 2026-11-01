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

public sealed class AuthService(
    IAccountRepository accounts,
    IMembershipRepository memberships,
    IAgencyRepository agencies,
    IRefreshTokenRepository refreshTokens,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokens,
    ITenantContext tenant,
    IOptions<JwtSettings> jwtOptions,
    TimeProvider time,
    ILogger<AuthService> logger) : IAuthService
{
    public const string DefaultWorkspaceName = "Main workspace";

    // Longer than any token we hand out; anything bigger is junk and is not worth hashing.
    private const int MaxRefreshTokenLength = 256;

    public async Task<AuthResult> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();

        // Validate() has accepted the text, so it names a business model.
        var businessModel = BusinessModelRules.TryParse(request.BusinessModel, out var parsed)
            ? parsed
            : throw new InvalidOperationException("The business model was validated but could not be read.");

        var account = new NewAccount(
            UserId: Guid.NewGuid(),
            Email: EmailAddressHelper.Normalize(request.Email),
            PasswordHash: passwordHasher.Hash(request.Password!),
            FullName: request.FullName?.Trim() ?? string.Empty,
            AgencyId: Guid.NewGuid(),
            AgencyName: request.OrganizationName!.Trim(),
            BusinessModel: businessModel,
            Role: businessModel.FounderRole(),
            WorkspaceId: Guid.NewGuid(),
            WorkspaceName: string.IsNullOrWhiteSpace(request.WorkspaceName) ? DefaultWorkspaceName : request.WorkspaceName.Trim());

        return await CreateAccountAndStartSessionAsync(account, cancellationToken);
    }

    public async Task<AuthResult> RegisterExternalAsync(ExternalRegistration registration, CancellationToken cancellationToken)
    {
        registration.Validate().ThrowIfAny();

        var email = EmailAddressHelper.Normalize(registration.Email);
        var businessModel = registration.BusinessModel;
        var account = new NewAccount(
            UserId: Guid.NewGuid(),
            Email: email,
            PasswordHash: null,
            FullName: registration.FullName?.Trim() ?? string.Empty,
            AgencyId: Guid.NewGuid(),
            AgencyName: registration.OrganizationName!.Trim(),
            BusinessModel: businessModel,
            Role: businessModel.FounderRole(),
            WorkspaceId: Guid.NewGuid(),
            WorkspaceName: DefaultWorkspaceName,
            Identity: new ExternalIdentity(registration.Provider, registration.Subject, email));

        return await CreateAccountAndStartSessionAsync(account, cancellationToken);
    }

    private async Task<AuthResult> CreateAccountAndStartSessionAsync(NewAccount account, CancellationToken cancellationToken)
    {
        if (!await accounts.CreateAccountAsync(account, cancellationToken))
        {
            throw new ConflictException("An account with this email already exists.", "email_taken");
        }

        logger.LogInformation(
            "Registered user {UserId} with new {BusinessModel} organization {AgencyId}", account.UserId, account.BusinessModel, account.AgencyId);
        return await StartSessionAsync(account.UserId, account.AgencyId, account.Role, cancellationToken);
    }

    public async Task<AuthResult> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();

        var user = await accounts.FindLoginByEmailAsync(EmailAddressHelper.Normalize(request.Email), cancellationToken);

        // Always verify something, so an unknown email costs the same time as a wrong password. A person
        // who signed up with Google has no password hash: nothing they type can match, and the answer is
        // the same 401 as for any wrong password.
        var storedHash = user?.PasswordHash;
        var passwordMatches = passwordHasher.Verify(request.Password!, storedHash ?? passwordHasher.DummyHash);
        if (user is null || storedHash is null || !passwordMatches)
        {
            throw new UnauthorizedException("Email or password is incorrect.", "invalid_credentials");
        }

        return await SignInToMembershipAsync(user.Id, request.AgencyId, cancellationToken);
    }

    public Task<AuthResult> LoginExternalAsync(Guid userId, CancellationToken cancellationToken) =>
        SignInToMembershipAsync(userId, null, cancellationToken);

    private async Task<AuthResult> SignInToMembershipAsync(Guid userId, Guid? requestedAgencyId, CancellationToken cancellationToken)
    {
        var userMemberships = await memberships.ListForUserAsync(userId, cancellationToken);
        if (userMemberships.Count == 0)
        {
            throw new ForbiddenException("This account does not belong to any organization.", "no_agency");
        }

        // The list is ordered most recently used first, so the first entry is the default.
        var membership = requestedAgencyId is { } requested
            ? userMemberships.FirstOrDefault(m => m.AgencyId == requested)
              ?? throw new ForbiddenException("You are not a member of that organization.", "not_a_member")
            : userMemberships[0];

        await memberships.TouchAsync(membership.AgencyId, userId, time.GetUtcNow(), cancellationToken);

        logger.LogInformation("User {UserId} signed in to agency {AgencyId}", userId, membership.AgencyId);
        return await StartSessionAsync(userId, membership.AgencyId, membership.Role, cancellationToken);
    }

    public async Task<AuthResult> RefreshAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        var presented = await FindRefreshTokenAsync(refreshToken, cancellationToken) ?? throw InvalidRefreshToken();
        var now = time.GetUtcNow();

        if (presented.RevokedAt is not null)
        {
            // A token that was already rotated or revoked is being used again: it was copied. We cannot
            // tell the thief from the owner, so every live token of this user for this agency ends.
            await RevokeAllAfterReuseAsync(presented, now, cancellationToken);
            throw InvalidRefreshToken();
        }

        if (presented.ExpiresAt <= now)
        {
            throw InvalidRefreshToken();
        }

        // The role comes from the database, not the old token, so a changed role or a removal takes
        // effect at the next refresh at the latest.
        var membership = await memberships.FindAsync(presented.UserId, presented.AgencyId, cancellationToken);
        if (membership is null)
        {
            await refreshTokens.RevokeAllAsync(presented.AgencyId, presented.UserId, now, cancellationToken);
            throw InvalidRefreshToken();
        }

        var (rawToken, replacement) = NewRefreshToken(presented.UserId, presented.AgencyId, now);
        if (!await refreshTokens.RotateAsync(presented.Id, replacement, now, cancellationToken))
        {
            // Someone else revoked this token between our check and the rotation: same as reuse.
            await RevokeAllAfterReuseAsync(presented, now, cancellationToken);
            throw InvalidRefreshToken();
        }

        return BuildResult(presented.UserId, presented.AgencyId, membership.Role, rawToken, replacement.ExpiresAt);
    }

    public async Task LogoutAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        var presented = await FindRefreshTokenAsync(refreshToken, cancellationToken);
        if (presented is null)
        {
            return;
        }

        await refreshTokens.RevokeAsync(presented.AgencyId, presented.UserId, presented.Id, time.GetUtcNow(), cancellationToken);
    }

    public async Task<AuthResult> SwitchAgencyAsync(
        SwitchAgencyRequest request, string? currentRefreshToken, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();

        var userId = tenant.RequireUserId();
        var targetAgencyId = request.AgencyId!.Value;

        var membership = await memberships.FindAsync(userId, targetAgencyId, cancellationToken)
                         ?? throw new ForbiddenException("You are not a member of that organization.", "not_a_member");

        var now = time.GetUtcNow();
        await memberships.TouchAsync(targetAgencyId, userId, now, cancellationToken);

        // The browser keeps one refresh cookie. It is replaced below, so end the old agency's token
        // instead of leaving it alive and unreachable. Only the caller's own token is touched.
        var current = await FindRefreshTokenAsync(currentRefreshToken, cancellationToken);
        if (current is not null && current.UserId == userId)
        {
            await refreshTokens.RevokeAsync(current.AgencyId, current.UserId, current.Id, now, cancellationToken);
        }

        logger.LogInformation("User {UserId} switched to agency {AgencyId}", userId, targetAgencyId);
        return await StartSessionAsync(userId, targetAgencyId, membership.Role, cancellationToken);
    }

    public async Task<MeResponse> GetMeAsync(CancellationToken cancellationToken)
    {
        var userId = tenant.RequireUserId();
        var agencyId = tenant.RequireAgencyId();

        var user = await accounts.GetUserAsync(userId, cancellationToken)
                   ?? throw new UnauthorizedException("The account no longer exists.", "unknown_user");
        var all = await memberships.ListForUserAsync(userId, cancellationToken);

        // The token can outlive a membership by up to its lifetime; the database has the final say.
        var current = all.FirstOrDefault(m => m.AgencyId == agencyId)
                      ?? throw new UnauthorizedException("You are no longer a member of this organization.", "membership_ended");
        var agency = await agencies.GetAsync(agencyId, userId, cancellationToken)
                     ?? throw new UnauthorizedException("You are no longer a member of this organization.", "membership_ended");

        return new MeResponse(
            new UserDto(user.Id, user.Email, user.FullName),
            new AgencyDto(agency.Id, agency.Name, agency.BusinessModel, agency.Plan),
            current.Role,
            all.Select(m => new MembershipDto(m.AgencyId, m.AgencyName, m.BusinessModel, m.Role)).ToList());
    }

    private async Task<AuthResult> StartSessionAsync(Guid userId, Guid agencyId, OrganizationRole role, CancellationToken cancellationToken)
    {
        var (rawToken, token) = NewRefreshToken(userId, agencyId, time.GetUtcNow());
        await refreshTokens.InsertAsync(token, cancellationToken);
        return BuildResult(userId, agencyId, role, rawToken, token.ExpiresAt);
    }

    private (string Raw, NewRefreshToken Stored) NewRefreshToken(Guid userId, Guid agencyId, DateTimeOffset now)
    {
        // Only the hash is stored. The raw value exists here and in the cookie we send, nowhere else.
        var raw = TokenGenerator.Create(TokenGenerator.DefaultByteLength);
        var stored = new NewRefreshToken(
            Guid.NewGuid(), agencyId, userId, HashHelper.Sha256Hex(raw), now.AddDays(jwtOptions.Value.RefreshTokenDays));
        return (raw, stored);
    }

    private AuthResult BuildResult(Guid userId, Guid agencyId, OrganizationRole role, string rawRefreshToken, DateTimeOffset refreshExpiresAt)
    {
        var access = accessTokens.Issue(userId, agencyId, role);
        var expiresIn = jwtOptions.Value.AccessTokenMinutes * 60;
        return new AuthResult(new TokenResponse(access.Value, TokenResponse.BearerType, expiresIn), rawRefreshToken, refreshExpiresAt);
    }

    private async Task<RefreshTokenRecord?> FindRefreshTokenAsync(string? refreshToken, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(refreshToken) || refreshToken.Length > MaxRefreshTokenLength)
        {
            return null;
        }

        return await refreshTokens.FindByHashAsync(HashHelper.Sha256Hex(refreshToken), cancellationToken);
    }

    private async Task RevokeAllAfterReuseAsync(RefreshTokenRecord presented, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var revoked = await refreshTokens.RevokeAllAsync(presented.AgencyId, presented.UserId, now, cancellationToken);
        logger.LogWarning(
            "A revoked refresh token was presented again for user {UserId} in agency {AgencyId}; revoked {Count} live tokens",
            presented.UserId, presented.AgencyId, revoked);
    }

    private static UnauthorizedException InvalidRefreshToken() =>
        new("Your session has expired. Sign in again.", "invalid_refresh_token");
}
