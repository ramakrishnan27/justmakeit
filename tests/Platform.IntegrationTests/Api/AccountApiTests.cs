using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// A signed-in member changes their own password, end to end over a real database. The other sessions of the person end,
/// the one that asked gets a fresh cookie, and nothing about the passwords is ever logged. The API connects as
/// <c>app_user</c>, as in production.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Docker")]
public sealed class AccountApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private const string NewPassword = "a brand new password 1";

    private readonly CapturingLoggerProvider _logs = new();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddLogging(builder => builder.AddProvider(_logs));

    private async Task<HttpResponseMessage> ChangeAsync(string cookie, string? current, string? next) =>
        await Host.PostAsync("/v1/auth/change-password", new { currentPassword = current, newPassword = next }, tokenCookie: cookie);

    private async Task<HttpResponseMessage> MeAsync(string cookie) => await Host.GetAsync("/v1/auth/me", tokenCookie: cookie);

    [Fact]
    public async Task ChangingThePassword_EndsEveryOtherSession_KeepsThisOne_AndTheNewPasswordSignsIn()
    {
        var first = await Host.RegisterAsync(businessModel: "company");
        var second = await Host.LoginAsync(first.Email, first.Password); // another device
        var bystander = await Host.RegisterAsync(businessModel: "company");
        // A token states when it was issued in whole seconds and the end is placed one second back, so a session started
        // within the last second before the change could survive it. A person takes longer than that; a test has to wait.
        await Task.Delay(TimeSpan.FromMilliseconds(1300));

        var changed = await ChangeAsync(second.AccessToken, first.Password, NewPassword);

        AssertStatus(HttpStatusCode.OK, changed);
        var fresh = ApiTestHost.MemberCookieValue(changed);
        Assert.False(string.IsNullOrEmpty(fresh));
        Assert.Contains("no-store", changed.Headers.CacheControl?.ToString() ?? string.Empty);
        // The body says only when the session ends: never a token, and neither password.
        var body = await ApiTestHost.ReadJsonAsync(changed);
        Assert.Equal(["expiresAt"], body.EnumerateObject().Select(p => p.Name));

        // The session that asked goes on with its fresh cookie; every older one of the person is void; nobody else is touched.
        AssertStatus(HttpStatusCode.OK, await MeAsync(fresh!));
        foreach (var older in new[] { first.AccessToken, second.AccessToken })
        {
            var refused = await MeAsync(older);
            AssertStatus(HttpStatusCode.Unauthorized, refused);
            Assert.Equal("session_ended", await CodeOfAsync(refused));
        }

        AssertStatus(HttpStatusCode.OK, await MeAsync(bystander.AccessToken));

        // The old password is gone and the new one signs in.
        var oldLogin = await Host.PostAsync("/v1/auth/login", new { email = first.Email, password = first.Password });
        AssertStatus(HttpStatusCode.Unauthorized, oldLogin);
        Assert.Equal("invalid_credentials", await CodeOfAsync(oldLogin));
        var again = await Host.LoginAsync(first.Email, NewPassword);
        AssertStatus(HttpStatusCode.OK, await MeAsync(again.AccessToken));
    }

    [Fact]
    public async Task AWrongCurrentPassword_Is400OnTheCurrentPasswordField_Not401_AndNothingChanges()
    {
        var admin = await Host.RegisterAsync(businessModel: "company");

        var response = await ChangeAsync(admin.AccessToken, "not the password", NewPassword);

        // Never a 401: the web client takes any 401 on a signed-in call as the end of the session.
        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("currentPassword", out _));
        AssertStatus(HttpStatusCode.OK, await MeAsync(admin.AccessToken));
        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/login", new { email = admin.Email, password = admin.Password }));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.PostAsync("/v1/auth/login", new { email = admin.Email, password = NewPassword }));
    }

    [Theory]
    [InlineData("same", "newPassword")]
    [InlineData("short", "newPassword")]
    [InlineData("missing", "newPassword")]
    public async Task ARejectedNewPassword_Is400OnTheNewPasswordField_AndNothingChanges(string kind, string field)
    {
        var admin = await Host.RegisterAsync(businessModel: "company");
        var next = kind switch { "same" => admin.Password, "short" => "too short", _ => null };

        var response = await ChangeAsync(admin.AccessToken, admin.Password, next);

        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty(field, out _));
        AssertStatus(HttpStatusCode.OK, await MeAsync(admin.AccessToken));
    }

    [Fact]
    public async Task WithoutASignIn_ThereIsNoSuchEndpoint_AndASiteAdminTokenIsNotAMember()
    {
        AssertStatus(HttpStatusCode.Unauthorized, await Host.PostAsync("/v1/auth/change-password", new { currentPassword = "x", newPassword = NewPassword }));

        var siteAdmin = Host.Factory.CreateSiteAdminAccessToken(Guid.NewGuid());
        var response = await Host.PostAsync("/v1/auth/change-password", new { currentPassword = "x", newPassword = NewPassword }, tokenCookie: siteAdmin);

        Assert.False(response.IsSuccessStatusCode, $"Answered {(int)response.StatusCode}.");
    }

    [Fact]
    public async Task AnAccountThatSignsInWithGoogle_HasNoPasswordToChange_409()
    {
        var admin = await Host.RegisterAsync(businessModel: "creator");
        // The way a Google account looks in the database: a linked identity, and no password.
        await OwnerExecuteAsync(
            "INSERT INTO user_identities (user_id, provider, subject, email) VALUES (@id, 'google', @subject, @email)",
            new { id = admin.UserId, subject = Guid.NewGuid().ToString("N"), email = admin.Email });
        await OwnerExecuteAsync("UPDATE users SET password_hash = NULL WHERE id = @id", new { id = admin.UserId });

        var response = await ChangeAsync(admin.AccessToken, "anything", NewPassword);

        AssertStatus(HttpStatusCode.Conflict, response);
        Assert.Equal("no_password", await CodeOfAsync(response));
        AssertStatus(HttpStatusCode.OK, await MeAsync(admin.AccessToken));
    }

    [Fact]
    public async Task ADeactivatedOrRemovedMember_CannotChangeAPassword_BecauseTheirTokenIsAlreadyVoid()
    {
        var admin = await Host.RegisterAsync(businessModel: "company");
        var invite = await Host.PostAsync("/v1/members/invitations", new { fullName = "Eve", email = $"eve-{Guid.NewGuid():N}@example.com", role = "contentwriter" }, tokenCookie: admin.AccessToken);
        var token = (await ApiTestHost.ReadJsonAsync(invite)).GetProperty("token").GetString()!;
        var accepted = await Host.PostAsync("/v1/auth/invitations/accept", new { token, fullName = "Eve", password = "eve's first password" });
        var eve = ApiTestHost.MemberCookieValue(accepted)!;
        var eveId = Guid.Parse((await ApiTestHost.ReadJsonAsync(await MeAsync(eve))).GetProperty("user").GetProperty("id").GetString()!);

        AssertStatus(HttpStatusCode.NoContent, await Host.PutAsync($"/v1/members/{eveId}/status", new { isActive = false }, admin.AccessToken));

        var response = await ChangeAsync(eve, "eve's first password", NewPassword);

        AssertStatus(HttpStatusCode.Unauthorized, response);
        Assert.Equal("session_ended", await CodeOfAsync(response));
    }

    [Fact]
    public async Task NeitherPasswordIsEverLogged()
    {
        var admin = await Host.RegisterAsync(businessModel: "company");
        await ChangeAsync(admin.AccessToken, "a wrong current password", NewPassword); // a refusal is logged too
        await Task.Delay(TimeSpan.FromMilliseconds(1300));
        var changed = await ChangeAsync(admin.AccessToken, admin.Password, NewPassword);
        var fresh = ApiTestHost.MemberCookieValue(changed)!;

        var everything = string.Join('\n', _logs.Messages);

        Assert.NotEmpty(_logs.Messages);
        foreach (var secret in new[] { admin.Password, NewPassword, "a wrong current password", admin.Email, fresh, admin.AccessToken })
        {
            Assert.DoesNotContain(secret, everything, StringComparison.OrdinalIgnoreCase);
        }
    }
}
