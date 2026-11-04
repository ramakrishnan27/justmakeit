using System.Net;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The second part of <see cref="TeamApiTests"/>: the name asked for when adding someone, activating a waiting invitation by
/// hand with a default password, and deactivating and activating a member. Same host, same database, same helpers.
/// </summary>
public sealed partial class TeamApiTests
{
    private const string DefaultPassword = "the default password 1";

    private async Task<HttpResponseMessage> ActivateAsync(Session admin, Guid invitationId, string? password = DefaultPassword) =>
        await Host.PostAsync($"/v1/members/invitations/{invitationId}/activate", new { password }, tokenCookie: admin.AccessToken);

    private async Task<HttpResponseMessage> SetActiveAsync(Session admin, Guid userId, bool isActive) =>
        await Host.PutAsync($"/v1/members/{userId}/status", new { isActive }, admin.AccessToken);

    private static System.Text.Json.JsonElement MemberIn(System.Text.Json.JsonElement team, Guid userId) =>
        team.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("userId").GetGuid() == userId);

    // --- The name ----------------------------------------------------------------------------

    [Fact]
    public async Task AnInvitation_CarriesTheNameTheAdminTyped_ToTheListAndToThePageTheLinkOpens()
    {
        var admin = await RegisterAsync("company");
        var email = NewEmail();

        var response = await Host.PostAsync(
            "/v1/members/invitations", new { fullName = "  Dev Kumar ", email, role = "contentwriter" }, tokenCookie: admin.AccessToken);
        AssertStatus(HttpStatusCode.Created, response);
        var created = await ApiTestHost.ReadJsonAsync(response);
        var token = created.GetProperty("token").GetString()!;
        Assert.Equal("Dev Kumar", created.GetProperty("invitation").GetProperty("fullName").GetString());

        var listed = (await TeamOfAsync(admin)).GetProperty("invitations")[0];
        Assert.Equal("Dev Kumar", listed.GetProperty("fullName").GetString());

        var preview = await ApiTestHost.ReadJsonAsync(await Host.PostAsync("/v1/auth/invitations/preview", new { token }));
        Assert.Equal("Dev Kumar", preview.GetProperty("fullName").GetString());

        // The person may still choose how their name is written.
        AssertStatus(HttpStatusCode.Created, await AcceptAsync(token, fullName: "Devi Kumar"));
        var me = await ApiTestHost.ReadJsonAsync(await MeAsync((await Host.LoginAsync(email, EmployeePassword)).AccessToken));
        Assert.Equal("Devi Kumar", me.GetProperty("user").GetProperty("fullName").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvitingWithoutAName_Is400WithTheNameField_AndNothingIsMade(string? fullName)
    {
        var admin = await RegisterAsync("company");

        var response = await Host.PostAsync(
            "/v1/members/invitations", new { fullName, email = NewEmail(), role = "contentwriter" }, tokenCookie: admin.AccessToken);

        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("fullName", out _));
        Assert.Equal(0, (await TeamOfAsync(admin)).GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task EveryoneInTheListIsActive_UntilSomeoneDeactivatesThem()
    {
        var admin = await RegisterAsync("company");
        var employee = await AddEmployeeAsync(admin, "contentwriter");

        var team = await TeamOfAsync(admin);

        Assert.All(team.GetProperty("members").EnumerateArray(), member => Assert.True(member.GetProperty("isActive").GetBoolean()));
        Assert.Equal(employee.UserId, MemberIn(team, employee.UserId).GetProperty("userId").GetGuid());
    }

    // --- Activating an invitation by hand ----------------------------------------------------

    [Fact]
    public async Task AnAdmin_ActivatesAWaitingInvitation_WithADefaultPassword_AndThePersonSignsInWithIt()
    {
        var admin = await RegisterAsync("agency");
        var email = NewEmail();
        var (token, invitationId) = await InviteAsync(admin, email, "clientmanager");

        var activated = await ActivateAsync(admin, invitationId);

        AssertStatus(HttpStatusCode.NoContent, activated);
        Assert.Equal(string.Empty, await activated.Content.ReadAsStringAsync()); // not even the password comes back
        var team = await TeamOfAsync(admin);
        Assert.Equal(0, team.GetProperty("invitations").GetArrayLength());
        var person = team.GetProperty("members").EnumerateArray().Single(m => m.GetProperty("email").GetString() == email);
        Assert.Equal("Test Person", person.GetProperty("fullName").GetString());
        Assert.Equal("clientmanager", person.GetProperty("role").GetString());
        Assert.True(person.GetProperty("isActive").GetBoolean());

        // They sign in with their address and the default password, as the invited role.
        var session = await Host.LoginAsync(email, DefaultPassword);
        var me = await ApiTestHost.ReadJsonAsync(await MeAsync(session.AccessToken));
        Assert.Equal("clientmanager", me.GetProperty("role").GetString());
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: session.AccessToken));
        // ... and are not a manager of the team.
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/members", tokenCookie: session.AccessToken));

        // The link of the invitation no longer works.
        var preview = await Host.PostAsync("/v1/auth/invitations/preview", new { token });
        AssertStatus(HttpStatusCode.NotFound, preview);
        Assert.Equal("invitation_invalid", await CodeOfAsync(preview));
        var accept = await AcceptAsync(token);
        AssertStatus(HttpStatusCode.NotFound, accept);
        Assert.Equal("invitation_invalid", await CodeOfAsync(accept));
    }

    [Fact]
    public async Task AnExpiredInvitation_CanStillBeActivatedByTheAdmin()
    {
        var admin = await RegisterAsync("company");
        var email = NewEmail();
        var (_, invitationId) = await InviteAsync(admin, email, "contentwriter");
        await OwnerExecuteAsync(
            "UPDATE member_invitations SET created_at = now() - interval '10 days', expires_at = now() - interval '1 day' WHERE id = @id",
            new { id = invitationId });
        Assert.True((await TeamOfAsync(admin)).GetProperty("invitations")[0].GetProperty("isExpired").GetBoolean());

        AssertStatus(HttpStatusCode.NoContent, await ActivateAsync(admin, invitationId));

        AssertStatus(HttpStatusCode.OK, await MeAsync((await Host.LoginAsync(email, DefaultPassword)).AccessToken));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("too short")]
    public async Task ActivatingWithAPasswordThatIsNotAllowed_Is400WithThePasswordField_AndTheInvitationStaysWaiting(string? password)
    {
        var admin = await RegisterAsync("company");
        var (_, invitationId) = await InviteAsync(admin, NewEmail(), "contentwriter");

        var response = await ActivateAsync(admin, invitationId, password);

        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("password", out _));
        Assert.Equal(1, (await TeamOfAsync(admin)).GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task Activating_Refuses_AnInvitationOfAnotherOrganization_AUsedOne_ACancelledOne_AndAnUnknownId()
    {
        var a = await RegisterAsync("company");
        var b = await RegisterAsync("company");
        var (_, foreign) = await InviteAsync(b, NewEmail(), "contentwriter");
        var (usedToken, used) = await InviteAsync(a, NewEmail(), "contentwriter");
        AssertStatus(HttpStatusCode.Created, await AcceptAsync(usedToken));
        var (_, cancelled) = await InviteAsync(a, NewEmail(), "contentwriter");
        AssertStatus(HttpStatusCode.NoContent, await Host.DeleteAsync($"/v1/members/invitations/{cancelled}", a.AccessToken));

        foreach (var id in new[] { foreign, used, cancelled, Guid.NewGuid() })
        {
            var response = await ActivateAsync(a, id);

            AssertStatus(HttpStatusCode.NotFound, response);
            Assert.Equal("invitation_not_found", await CodeOfAsync(response));
        }

        Assert.Equal(1, (await TeamOfAsync(b)).GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task Activating_ForAnAddressThatBecameAnAccountMeanwhile_Is409EmailTaken_AndTheInvitationStays()
    {
        var admin = await RegisterAsync("company");
        var email = NewEmail();
        var (_, invitationId) = await InviteAsync(admin, email, "contentwriter");
        await Host.RegisterAsync(email: email, agencyName: "Somebody Else", businessModel: "creator");

        var response = await ActivateAsync(admin, invitationId);

        AssertStatus(HttpStatusCode.Conflict, response);
        Assert.Equal("email_taken", await CodeOfAsync(response));
        Assert.Equal(1, (await TeamOfAsync(admin)).GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task OnlyAManagerMayActivate_AndAnotherRolesTokenIsRefused()
    {
        var admin = await RegisterAsync("company");
        var writer = await AddEmployeeAsync(admin, "contentwriter");
        var (_, invitationId) = await InviteAsync(admin, NewEmail(), "contentwriter");

        var response = await ActivateAsync(writer, invitationId);

        AssertStatus(HttpStatusCode.Forbidden, response);
        Assert.Equal(1, (await TeamOfAsync(admin)).GetProperty("invitations").GetArrayLength());
    }

    [Fact]
    public async Task ActivatingByHand_NeverLogsThePasswordTheAddressOrTheLink()
    {
        var admin = await RegisterAsync("company");
        var email = NewEmail();
        var (token, invitationId) = await InviteAsync(admin, email, "contentwriter");
        await ActivateAsync(admin, invitationId, "too short"); // a refusal is logged too
        await ActivateAsync(admin, invitationId);
        var session = await Host.LoginAsync(email, DefaultPassword);
        await Host.PostAsync("/v1/auth/login", new { email, password = "the wrong password 1" });

        var everything = string.Join('\n', _logs.Messages);

        Assert.NotEmpty(_logs.Messages);
        foreach (var secret in new[] { DefaultPassword, "too short", "the wrong password 1", token, email, email.ToUpperInvariant(), session.AccessToken })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.OrdinalIgnoreCase);
        }
    }

    // --- Deactivating and activating a member ------------------------------------------------

    [Fact]
    public async Task ADeactivatedMember_IsSignedOutAtOnce_CannotSignIn_AndSignsInAgainOnceActivated()
    {
        var admin = await RegisterAsync("agency");
        var employee = await AddEmployeeAsync(admin, "marketingmanager");
        var bystander = await AddEmployeeAsync(admin, "contentwriter");
        AssertStatus(HttpStatusCode.OK, await MeAsync(employee.AccessToken));

        AssertStatus(HttpStatusCode.NoContent, await SetActiveAsync(admin, employee.UserId, isActive: false));

        // Their token is void at once; nobody else is touched; the list says so.
        var refused = await MeAsync(employee.AccessToken);
        AssertStatus(HttpStatusCode.Unauthorized, refused);
        Assert.Equal("session_ended", await CodeOfAsync(refused));
        AssertStatus(HttpStatusCode.OK, await MeAsync(bystander.AccessToken));
        AssertStatus(HttpStatusCode.OK, await MeAsync(admin.AccessToken));
        Assert.False(MemberIn(await TeamOfAsync(admin), employee.UserId).GetProperty("isActive").GetBoolean());

        // With the right password they are told why; with a wrong one they are told nothing more than for anyone.
        var login = await Host.PostAsync("/v1/auth/login", new { email = employee.Email, password = EmployeePassword });
        AssertStatus(HttpStatusCode.Forbidden, login);
        Assert.Equal("account_deactivated", await CodeOfAsync(login));
        var wrong = await Host.PostAsync("/v1/auth/login", new { email = employee.Email, password = "not the password 1" });
        AssertStatus(HttpStatusCode.Unauthorized, wrong);
        Assert.Equal("invalid_credentials", await CodeOfAsync(wrong));

        // Activating lets them in again. A token says when it was issued in whole seconds, so signing in within the same
        // second as the deactivation is refused (the safe side): a person takes longer than that, a test has to wait.
        AssertStatus(HttpStatusCode.NoContent, await SetActiveAsync(admin, employee.UserId, isActive: true));
        await Task.Delay(TimeSpan.FromMilliseconds(1100));
        var again = await Host.LoginAsync(employee.Email, EmployeePassword);
        AssertStatus(HttpStatusCode.OK, await MeAsync(again.AccessToken));
        Assert.True(MemberIn(await TeamOfAsync(admin), employee.UserId).GetProperty("isActive").GetBoolean());
        // The token from before the deactivation stays void.
        AssertStatus(HttpStatusCode.Unauthorized, await MeAsync(employee.AccessToken));
    }

    [Fact]
    public async Task ADeactivatedMember_StaysInTheTeam_WithTheirRole_AndCanBeRemovedOrReRoledLikeAnyOther()
    {
        var admin = await RegisterAsync("agency");
        var employee = await AddEmployeeAsync(admin, "contentwriter");
        AssertStatus(HttpStatusCode.NoContent, await SetActiveAsync(admin, employee.UserId, isActive: false));

        var member = MemberIn(await TeamOfAsync(admin), employee.UserId);
        Assert.Equal("contentwriter", member.GetProperty("role").GetString());

        AssertStatus(HttpStatusCode.NoContent, await Host.PutAsync($"/v1/members/{employee.UserId}/role", new { role = "clientmanager" }, admin.AccessToken));
        Assert.False(MemberIn(await TeamOfAsync(admin), employee.UserId).GetProperty("isActive").GetBoolean());

        AssertStatus(HttpStatusCode.NoContent, await Host.DeleteAsync($"/v1/members/{employee.UserId}", admin.AccessToken));
        Assert.Equal(["organizationadmin"], RolesIn(await TeamOfAsync(admin)));
    }

    [Fact]
    public async Task Deactivating_Refuses_YourselfAStrangerAndAnUnknownPerson_AndASecondAdminMayBeDeactivated()
    {
        var a = await RegisterAsync("company");
        var b = await RegisterAsync("company");
        var strangersEmployee = await AddEmployeeAsync(b, "contentwriter");

        var self = await SetActiveAsync(a, a.UserId, isActive: false);
        AssertStatus(HttpStatusCode.Conflict, self);
        Assert.Equal("cannot_change_self", await CodeOfAsync(self));

        foreach (var id in new[] { strangersEmployee.UserId, Guid.NewGuid() })
        {
            var missing = await SetActiveAsync(a, id, isActive: false);
            AssertStatus(HttpStatusCode.NotFound, missing);
            Assert.Equal("member_not_found", await CodeOfAsync(missing));
        }

        AssertStatus(HttpStatusCode.OK, await MeAsync(strangersEmployee.AccessToken));

        // With two admins, the second may be deactivated: the first is still an active admin.
        var second = await AddEmployeeAsync(a, "organizationadmin");
        AssertStatus(HttpStatusCode.NoContent, await SetActiveAsync(a, second.UserId, isActive: false));
        AssertStatus(HttpStatusCode.OK, await MeAsync(a.AccessToken));
    }
}
