using Microsoft.Extensions.Logging;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Utilities;

namespace Platform.Auth.Services;

public sealed class AuthService(
    IAccountRepository accounts,
    IMembershipRepository memberships,
    IAgencyRepository agencies,
    IPasswordHasher passwordHasher,
    IAccessTokenIssuer accessTokens,
    IPermissionService permissions,
    ITenantContext tenant,
    TimeProvider time,
    ILogger<AuthService> logger) : IAuthService
{
    public const string DefaultWorkspaceName = "Main workspace";

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
        // Read before anything is written, so a failure here leaves no half-made account behind.
        var grants = await permissions.GetGrantsAsync(account.BusinessModel, account.Role, cancellationToken);

        if (!await accounts.CreateAccountAsync(account, cancellationToken))
        {
            throw new ConflictException("An account with this email already exists.", "email_taken");
        }

        logger.LogInformation(
            "Registered user {UserId} with new {BusinessModel} organization {AgencyId}", account.UserId, account.BusinessModel, account.AgencyId);
        return BuildResult(account.UserId, account.AgencyId, account.Role, grants);
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

        var grants = await permissions.GetGrantsAsync(membership.BusinessModel, membership.Role, cancellationToken);

        await memberships.TouchAsync(membership.AgencyId, userId, time.GetUtcNow(), cancellationToken);

        logger.LogInformation("User {UserId} signed in to agency {AgencyId}", userId, membership.AgencyId);
        return BuildResult(userId, membership.AgencyId, membership.Role, grants);
    }

    public async Task<AuthResult> SwitchAgencyAsync(SwitchAgencyRequest request, CancellationToken cancellationToken)
    {
        request.Validate().ThrowIfAny();

        var userId = tenant.RequireUserId();
        var targetAgencyId = request.AgencyId!.Value;

        var membership = await memberships.FindAsync(userId, targetAgencyId, cancellationToken)
                         ?? throw new ForbiddenException("You are not a member of that organization.", "not_a_member");

        var grants = await permissions.GetGrantsAsync(membership.BusinessModel, membership.Role, cancellationToken);

        await memberships.TouchAsync(targetAgencyId, userId, time.GetUtcNow(), cancellationToken);

        // The browser keeps one token cookie. The one issued here replaces it.
        logger.LogInformation("User {UserId} switched to agency {AgencyId}", userId, targetAgencyId);
        return BuildResult(userId, targetAgencyId, membership.Role, grants);
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

    // The one place a member's access token is made: register, login, switch-agency and Google all
    // end here, each with the permissions its caller read for the member's role. Nothing is stored:
    // the token is the whole session.
    private AuthResult BuildResult(Guid userId, Guid agencyId, OrganizationRole role, PermissionGrants grants)
    {
        var access = accessTokens.Issue(userId, agencyId, role, grants);
        return new AuthResult(access.Value, access.ExpiresAt);
    }
}
