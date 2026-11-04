using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Helpers.Constants;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The team of a company or an agency, end to end over a real database: an organization admin invites a person, the
/// person opens the link, picks a password and is signed in; roles change, people are removed, and a removed or
/// re-roled person's old token stops working at once. The API connects as <c>app_user</c>, as in production.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed partial class TeamApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private const string EmployeePassword = "employee password 123";

    private readonly CapturingLoggerProvider _logs = new();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddLogging(builder => builder.AddProvider(_logs));

    // --- Helpers -----------------------------------------------------------------------------

    private Task<Session> RegisterAsync(string businessModel = "company") => Host.RegisterAsync(businessModel: businessModel);

    private static string NewEmail() => $"employee-{Guid.NewGuid():N}@example.com";

    private async Task<(string Token, Guid InvitationId)> InviteAsync(Session admin, string email, string role)
    {
        var response = await Host.PostAsync("/v1/members/invitations", new { fullName = "Test Person", email, role }, tokenCookie: admin.AccessToken);
        AssertStatus(HttpStatusCode.Created, response);
        var body = await ApiTestHost.ReadJsonAsync(response);
        return (body.GetProperty("token").GetString()!, body.GetProperty("invitation").GetProperty("id").GetGuid());
    }

    private Task<HttpResponseMessage> AcceptAsync(string token, string fullName = "Eve Employee", string password = EmployeePassword) =>
        Host.PostAsync("/v1/auth/invitations/accept", new { token, fullName, password });

    /// <summary>Invites and accepts through the API. Returns the new member as a signed-in session.</summary>
    private async Task<Session> AddEmployeeAsync(Session admin, string role, string? email = null)
    {
        email ??= NewEmail();
        var (token, _) = await InviteAsync(admin, email, role);
        var response = await AcceptAsync(token);
        AssertStatus(HttpStatusCode.Created, response);
        return await Host.LoginAsync(email, EmployeePassword);
    }

    private async Task<JsonElement> TeamOfAsync(Session admin)
    {
        var response = await Host.GetAsync("/v1/members", tokenCookie: admin.AccessToken);
        AssertStatus(HttpStatusCode.OK, response);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private static string[] RolesIn(JsonElement team) =>
        [.. team.GetProperty("members").EnumerateArray().Select(m => m.GetProperty("role").GetString()!).Order()];

    private async Task<HttpResponseMessage> MeAsync(string cookie) => await Host.GetAsync("/v1/auth/me", tokenCookie: cookie);

    // --- The whole way in --------------------------------------------------------------------

    [Fact]
    public async Task AnInvitedPerson_OpensTheLink_ChoosesAPassword_AndIsSignedInAsTheInvitedRole()
    {
        var admin = await RegisterAsync("company");
        var email = NewEmail().ToUpperInvariant(); // the address is kept lower-case
        var (token, _) = await InviteAsync(admin, email, "marketingmanager");

        // The page asks what the link is for, with nobody signed in.
        var preview = await Host.PostAsync("/v1/auth/invitations/preview", new { token });
        AssertStatus(HttpStatusCode.OK, preview);
        var shown = await ApiTestHost.ReadJsonAsync(preview);
        Assert.Equal("Acme Agency", shown.GetProperty("organizationName").GetString());
        Assert.Equal("company", shown.GetProperty("businessModel").GetString());
        Assert.Equal("marketingmanager", shown.GetProperty("role").GetString());
        Assert.Equal(email.ToLowerInvariant(), shown.GetProperty("email").GetString());

        var accepted = await AcceptAsync(token, "  Eve Employee ");

        // Signed in by cookie, and the body says only when it ends: the token is never in a body.
        AssertStatus(HttpStatusCode.Created, accepted);
        var cookie = ApiTestHost.MemberCookieValue(accepted);
        Assert.NotNull(cookie);
        var header = ApiTestHost.MemberCookieHeader(accepted)!.ToLowerInvariant();
        Assert.Contains("httponly", header);
        Assert.Contains("secure", header);
        Assert.Contains("samesite=strict", header);
        var body = await accepted.Content.ReadAsStringAsync();
        Assert.Equal(["expiresAt"], JsonDocument.Parse(body).RootElement.EnumerateObject().Select(p => p.Name));
        Assert.DoesNotContain(cookie, body);
        Assert.DoesNotContain(token, body);

        // They are who the invitation said, in the organization that invited them.
        var me = await ApiTestHost.ReadJsonAsync(await MeAsync(cookie));
        Assert.Equal(email.ToLowerInvariant(), me.GetProperty("user").GetProperty("email").GetString());
        Assert.Equal("Eve Employee", me.GetProperty("user").GetProperty("fullName").GetString());
        Assert.Equal("marketingmanager", me.GetProperty("role").GetString());
        Assert.Equal(admin.AgencyId, me.GetProperty("agency").GetProperty("id").GetGuid());

        // They work with the permissions of that role: workspaces yes, the team no.
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: cookie));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/members", tokenCookie: cookie));

        // And they can sign in later with the password they chose.
        var login = await Host.PostAsync("/v1/auth/login", new { email = email.ToLowerInvariant(), password = EmployeePassword });
        AssertStatus(HttpStatusCode.OK, login);

        // The admin sees them in the team, and the invitation is gone from the waiting list.
        var team = await TeamOfAsync(admin);
        Assert.Equal(["marketingmanager", "organizationadmin"], RolesIn(team));
        Assert.Equal(0, team.GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task AnInvitationLink_WorksOnce()
    {
        var admin = await RegisterAsync();
        var (token, _) = await InviteAsync(admin, NewEmail(), "contentwriter");
        AssertStatus(HttpStatusCode.Created, await AcceptAsync(token));

        var again = await AcceptAsync(token, "Someone Else", "another password 456");
        AssertStatus(HttpStatusCode.NotFound, again);
        Assert.Equal("invitation_invalid", await CodeOfAsync(again));
        var preview = await Host.PostAsync("/v1/auth/invitations/preview", new { token });
        AssertStatus(HttpStatusCode.NotFound, preview);
    }

    [Theory]
    [InlineData("nonsense")]
    [InlineData("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA")]
    public async Task AGuessedLink_IsNotFound_WithTheSameAnswerAsAUsedOne(string token)
    {
        var preview = await Host.PostAsync("/v1/auth/invitations/preview", new { token });
        var accept = await AcceptAsync(token);

        AssertStatus(HttpStatusCode.NotFound, preview);
        AssertStatus(HttpStatusCode.NotFound, accept);
        Assert.Equal("invitation_invalid", await CodeOfAsync(preview));
        Assert.Equal("invitation_invalid", await CodeOfAsync(accept));
    }

    [Fact]
    public async Task AcceptingWithAShortPassword_IsRefused_AndTheLinkStillWorks()
    {
        var admin = await RegisterAsync();
        var (token, _) = await InviteAsync(admin, NewEmail(), "contentwriter");

        var refused = await AcceptAsync(token, password: "short");

        AssertStatus(HttpStatusCode.BadRequest, refused);
        Assert.True((await ApiTestHost.ReadJsonAsync(refused)).GetProperty("errors").TryGetProperty("password", out _));
        AssertStatus(HttpStatusCode.Created, await AcceptAsync(token));
    }

    // --- Inviting ----------------------------------------------------------------------------

    [Fact]
    public async Task AnAddressThatAlreadyHasAnAccount_CannotBeInvited_WhateverTheCase()
    {
        var admin = await RegisterAsync();
        var other = await RegisterAsync("agency");

        foreach (var email in new[] { other.Email, other.Email.ToUpperInvariant(), admin.Email })
        {
            var response = await Host.PostAsync("/v1/members/invitations", new { fullName = "Test Person", email, role = "contentwriter" }, tokenCookie: admin.AccessToken);

            AssertStatus(HttpStatusCode.Conflict, response);
            Assert.Equal("email_taken", await CodeOfAsync(response));
        }

        Assert.Equal(0, (await TeamOfAsync(admin)).GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task OnlyOneInvitationPerAddressWaits_AndAnExpiredOneIsReplaced()
    {
        var admin = await RegisterAsync();
        var email = NewEmail();
        var (first, firstId) = await InviteAsync(admin, email, "contentwriter");

        var second = await Host.PostAsync("/v1/members/invitations", new { fullName = "Test Person", email = email.ToUpperInvariant(), role = "marketingmanager" }, tokenCookie: admin.AccessToken);
        AssertStatus(HttpStatusCode.Conflict, second);
        Assert.Equal("invitation_exists", await CodeOfAsync(second));

        // Once it has run out, the address can be invited again and the old link is dead.
        await OwnerExecuteAsync(
            "UPDATE member_invitations SET created_at = now() - interval '8 days', expires_at = now() - interval '1 day' WHERE id = @firstId", new { firstId });
        var expiredPreview = await Host.PostAsync("/v1/auth/invitations/preview", new { token = first });
        AssertStatus(HttpStatusCode.NotFound, expiredPreview);
        Assert.True((await TeamOfAsync(admin)).GetProperty("invitations")[0].GetProperty("isExpired").GetBoolean());

        var (replacement, _) = await InviteAsync(admin, email, "marketingmanager");

        AssertStatus(HttpStatusCode.NotFound, await AcceptAsync(first));
        AssertStatus(HttpStatusCode.Created, await AcceptAsync(replacement));
        Assert.Equal(["marketingmanager", "organizationadmin"], RolesIn(await TeamOfAsync(admin)));
    }

    [Fact]
    public async Task ARoleTheModelDoesNotHave_IsRefusedWithTheField_ButAnAgencyMayHaveAClientManager()
    {
        var company = await RegisterAsync("company");
        var agency = await RegisterAsync("agency");

        var refused = await Host.PostAsync("/v1/members/invitations", new { fullName = "Test Person", email = NewEmail(), role = "clientmanager" }, tokenCookie: company.AccessToken);
        AssertStatus(HttpStatusCode.BadRequest, refused);
        Assert.True((await ApiTestHost.ReadJsonAsync(refused)).GetProperty("errors").TryGetProperty("role", out _));

        await InviteAsync(agency, NewEmail(), "clientmanager");
    }

    [Fact]
    public async Task ACreatorOrganization_CannotInvite_ByDefaultOrEvenWithThePermissionInTheToken()
    {
        var creator = await RegisterAsync("creator");

        // By default the creator's role does not hold the permission.
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/members", tokenCookie: creator.AccessToken));

        // And if the Site admin ever granted it, the model still refuses.
        var granted = Host.Factory.CreateAccessToken(
            creator.UserId, creator.AgencyId, OrganizationRole.ContentWriter, [PermissionKeys.DashboardView, PermissionKeys.MembersManage]);
        var response = await Host.PostAsync("/v1/members/invitations", new { fullName = "Test Person", email = NewEmail(), role = "contentwriter" }, tokenCookie: granted);
        AssertStatus(HttpStatusCode.Conflict, response);
        Assert.Equal("team_not_available", await CodeOfAsync(response));
    }

    [Fact]
    public async Task TheTeamList_NeverShowsTheSecretOrAHash_AndOnlyTheOrganizationsOwnPeople()
    {
        var a = await RegisterAsync("company");
        var b = await RegisterAsync("company");
        await InviteAsync(a, "waiting@example.com", "contentwriter");
        await AddEmployeeAsync(b, "contentwriter", "b-employee@example.com");

        var team = await TeamOfAsync(a);

        Assert.Equal(["organizationadmin"], RolesIn(team));
        var invitation = team.GetProperty("invitations")[0];
        Assert.Equal(["createdAt", "email", "expiresAt", "fullName", "id", "isExpired", "role"], invitation.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal(
            ["email", "fullName", "isActive", "isYou", "joinedAt", "role", "userId"],
            team.GetProperty("members")[0].EnumerateObject().Select(p => p.Name).Order());
        var raw = team.GetRawText();
        Assert.DoesNotContain("b-employee@example.com", raw);
        Assert.DoesNotContain("hash", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("token", raw, StringComparison.OrdinalIgnoreCase);
    }

    // --- Revoking ----------------------------------------------------------------------------

    [Fact]
    public async Task ARevokedInvitation_StopsWorking_AndAnotherOrganizationCannotRevokeIt()
    {
        var admin = await RegisterAsync("company");
        var stranger = await RegisterAsync("company");
        var (token, id) = await InviteAsync(admin, NewEmail(), "contentwriter");

        // Another organization's admin cannot even see it.
        var foreign = await Host.DeleteAsync($"/v1/members/invitations/{id}", stranger.AccessToken);
        AssertStatus(HttpStatusCode.NotFound, foreign);
        Assert.Equal("invitation_not_found", await CodeOfAsync(foreign));
        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/invitations/preview", new { token }));

        AssertStatus(HttpStatusCode.NoContent, await Host.DeleteAsync($"/v1/members/invitations/{id}", admin.AccessToken));

        AssertStatus(HttpStatusCode.NotFound, await Host.PostAsync("/v1/auth/invitations/preview", new { token }));
        AssertStatus(HttpStatusCode.NotFound, await AcceptAsync(token));
        AssertStatus(HttpStatusCode.NotFound, await Host.DeleteAsync($"/v1/members/invitations/{id}", admin.AccessToken));
        Assert.Equal(0, (await TeamOfAsync(admin)).GetProperty("invitations").GetArrayLength());
    }

    // --- Changing a role ---------------------------------------------------------------------

    [Fact]
    public async Task ChangingARole_EndsThatPersonsOldToken_AndTheyGetTheNewRoleWhenTheySignInAgain()
    {
        var admin = await RegisterAsync("agency");
        var employee = await AddEmployeeAsync(admin, "contentwriter");
        var bystander = await AddEmployeeAsync(admin, "contentwriter");
        AssertStatus(HttpStatusCode.OK, await MeAsync(employee.AccessToken));

        var changed = await Host.PutAsync($"/v1/members/{employee.UserId}/role", new { role = "clientmanager" }, admin.AccessToken);
        AssertStatus(HttpStatusCode.NoContent, changed);

        // The old token is void at once, from the cookie and from a header; nobody else is touched.
        var refused = await MeAsync(employee.AccessToken);
        AssertStatus(HttpStatusCode.Unauthorized, refused);
        Assert.Equal("session_ended", await CodeOfAsync(refused));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/auth/me", employee.AccessToken));
        AssertStatus(HttpStatusCode.OK, await MeAsync(bystander.AccessToken));
        AssertStatus(HttpStatusCode.OK, await MeAsync(admin.AccessToken));

        // A token says when it was issued in whole seconds, so signing in within the same second as the change is
        // refused too (the safe side). A person takes longer than that; a test has to wait for it.
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var again = await Host.LoginAsync(employee.Email, EmployeePassword);
        var me = await ApiTestHost.ReadJsonAsync(await MeAsync(again.AccessToken));
        Assert.Equal("clientmanager", me.GetProperty("role").GetString());
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: again.AccessToken));
    }

    [Fact]
    public async Task ChangingARole_Refuses_YourselfAStrangerAnUnknownPersonAndARoleTheModelDoesNotHave()
    {
        var a = await RegisterAsync("company");
        var b = await RegisterAsync("company");
        var employee = await AddEmployeeAsync(a, "contentwriter");
        var strangersEmployee = await AddEmployeeAsync(b, "contentwriter");

        async Task<HttpResponseMessage> Change(Guid id, string role) =>
            await Host.PutAsync($"/v1/members/{id}/role", new { role }, a.AccessToken);

        var self = await Change(a.UserId, "contentwriter");
        AssertStatus(HttpStatusCode.Conflict, self);
        Assert.Equal("cannot_change_self", await CodeOfAsync(self));

        foreach (var id in new[] { strangersEmployee.UserId, Guid.NewGuid() })
        {
            var missing = await Change(id, "contentwriter");
            AssertStatus(HttpStatusCode.NotFound, missing);
            Assert.Equal("member_not_found", await CodeOfAsync(missing));
        }

        var notInCompany = await Change(employee.UserId, "clientmanager");
        AssertStatus(HttpStatusCode.BadRequest, notInCompany);
        Assert.True((await ApiTestHost.ReadJsonAsync(notInCompany)).GetProperty("errors").TryGetProperty("role", out _));

        // Nothing changed, and nobody was signed out by a refused attempt.
        AssertStatus(HttpStatusCode.OK, await MeAsync(employee.AccessToken));
        AssertStatus(HttpStatusCode.OK, await MeAsync(strangersEmployee.AccessToken));
    }

    [Fact]
    public async Task TheLastOrganizationAdmin_CannotBeDemotedOrRemoved_EvenByARoleTheSiteAdminTrusted()
    {
        var admin = await RegisterAsync("company");
        var manager = await AddEmployeeAsync(admin, "marketingmanager");

        // The Site admin gave marketing managers of companies the team permission (what the editor does).
        await OwnerExecuteAsync(
            "UPDATE role_permissions SET is_granted = true WHERE business_model = 'company' AND role = 'marketingmanager' AND permission_key = 'members.manage'");
        var managerToken = Host.Factory.CreateAccessToken(
            manager.UserId, manager.AgencyId, OrganizationRole.MarketingManager, [PermissionKeys.DashboardView, PermissionKeys.MembersManage]);

        var demote = await Host.PutAsync($"/v1/members/{admin.UserId}/role", new { role = "contentwriter" }, managerToken);
        var remove = await Host.DeleteAsync($"/v1/members/{admin.UserId}", managerToken);

        AssertStatus(HttpStatusCode.Conflict, demote);
        Assert.Equal("last_organization_admin", await CodeOfAsync(demote));
        AssertStatus(HttpStatusCode.Conflict, remove);
        Assert.Equal("last_organization_admin", await CodeOfAsync(remove));
        AssertStatus(HttpStatusCode.OK, await MeAsync(admin.AccessToken));
    }

    [Fact]
    public async Task TheDatabaseChecksThePermissionToo_SoATokenThatClaimsItIsNotEnough()
    {
        var admin = await RegisterAsync("company");
        var writer = await AddEmployeeAsync(admin, "contentwriter");
        var target = await AddEmployeeAsync(admin, "contentwriter");

        // A token whose claims say members.manage, for a role the database does not grant it to.
        var forged = Host.Factory.CreateAccessToken(
            writer.UserId, writer.AgencyId, OrganizationRole.ContentWriter, [PermissionKeys.DashboardView, PermissionKeys.MembersManage]);

        var listed = await Host.GetAsync("/v1/members", forged);
        var removed = await Host.DeleteAsync($"/v1/members/{target.UserId}", forged);

        AssertStatus(HttpStatusCode.Forbidden, listed);
        AssertStatus(HttpStatusCode.Forbidden, removed);
        Assert.Equal("insufficient_permission", await CodeOfAsync(removed));
        AssertStatus(HttpStatusCode.OK, await MeAsync(target.AccessToken));
    }

    // --- Removing ----------------------------------------------------------------------------

    [Fact]
    public async Task RemovingAPerson_EndsTheirToken_DeletesTheAccount_AndFreesTheAddress()
    {
        var admin = await RegisterAsync("company");
        var email = NewEmail();
        var employee = await AddEmployeeAsync(admin, "marketingmanager", email);
        AssertStatus(HttpStatusCode.OK, await MeAsync(employee.AccessToken));

        AssertStatus(HttpStatusCode.NoContent, await Host.DeleteAsync($"/v1/members/{employee.UserId}", admin.AccessToken));

        var refused = await MeAsync(employee.AccessToken);
        AssertStatus(HttpStatusCode.Unauthorized, refused);
        Assert.Equal("session_ended", await CodeOfAsync(refused));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/workspaces", employee.AccessToken));

        // The account is gone: the password no longer signs in, and nothing of it is left in the organization.
        var login = await Host.PostAsync("/v1/auth/login", new { email, password = EmployeePassword });
        AssertStatus(HttpStatusCode.Unauthorized, login);
        Assert.Equal("invalid_credentials", await CodeOfAsync(login));
        Assert.Equal(0, await OwnerQueryAsync<long>("SELECT count(*) FROM users WHERE id = @id", new { id = employee.UserId }));
        Assert.Equal(["organizationadmin"], RolesIn(await TeamOfAsync(admin)));

        // The address is free: it can be invited again ...
        var (token, _) = await InviteAsync(admin, email, "contentwriter");
        AssertStatus(HttpStatusCode.Created, await AcceptAsync(token));
        // ... and the admin was never signed out.
        AssertStatus(HttpStatusCode.OK, await MeAsync(admin.AccessToken));
    }

    [Fact]
    public async Task ARemovedPersonsAddress_CanRegisterAnOrganizationOfItsOwn()
    {
        var admin = await RegisterAsync("agency");
        var email = NewEmail();
        var employee = await AddEmployeeAsync(admin, "contentwriter", email);
        AssertStatus(HttpStatusCode.NoContent, await Host.DeleteAsync($"/v1/members/{employee.UserId}", admin.AccessToken));

        var registered = await Host.PostAsync(
            "/v1/auth/register", new { email, password = "a brand new password", fullName = "Eve", organizationName = "Eve Studio", businessModel = "creator" });

        AssertStatus(HttpStatusCode.Created, registered);
    }

    [Fact]
    public async Task Removing_Refuses_YourselfAStrangerAndAnUnknownPerson()
    {
        var a = await RegisterAsync("company");
        var b = await RegisterAsync("company");
        var strangersEmployee = await AddEmployeeAsync(b, "contentwriter");

        var self = await Host.DeleteAsync($"/v1/members/{a.UserId}", a.AccessToken);
        AssertStatus(HttpStatusCode.Conflict, self);
        Assert.Equal("cannot_change_self", await CodeOfAsync(self));

        foreach (var id in new[] { strangersEmployee.UserId, Guid.NewGuid() })
        {
            var missing = await Host.DeleteAsync($"/v1/members/{id}", a.AccessToken);
            AssertStatus(HttpStatusCode.NotFound, missing);
            Assert.Equal("member_not_found", await CodeOfAsync(missing));
        }

        AssertStatus(HttpStatusCode.OK, await MeAsync(strangersEmployee.AccessToken));
        AssertStatus(HttpStatusCode.OK, await MeAsync(a.AccessToken));
    }

    // --- Nothing secret is logged ------------------------------------------------------------

    [Fact]
    public async Task NeitherTheLinkNorTheAddressNorThePasswordIsEverLogged()
    {
        var admin = await RegisterAsync("company");
        var email = NewEmail();
        var (token, id) = await InviteAsync(admin, email, "contentwriter");
        await Host.PostAsync("/v1/auth/invitations/preview", new { token });
        var accepted = await AcceptAsync(token);
        var cookie = ApiTestHost.MemberCookieValue(accepted)!;
        await Host.PostAsync("/v1/auth/invitations/accept", new { token = "a-guess-that-is-wrong", fullName = "X", password = EmployeePassword });
        await Host.DeleteAsync($"/v1/members/invitations/{id}", admin.AccessToken);

        var everything = string.Join('\n', _logs.Messages);

        Assert.NotEmpty(_logs.Messages);
        foreach (var secret in new[] { token, email, email.ToUpperInvariant(), EmployeePassword, cookie, "a-guess-that-is-wrong" })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.OrdinalIgnoreCase);
        }
    }

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<string> _messages = new();

        public IReadOnlyCollection<string> Messages => _messages;

        public ILogger CreateLogger(string categoryName) => new Capture(_messages);

        public void Dispose()
        {
        }

        private sealed class Capture(ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                messages.Enqueue(formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception));
        }
    }
}
