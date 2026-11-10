using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Platform.Auth.Services;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The Site admin's channels end to end over a real database: creating one with an uploaded icon, serving the icon, enabling a
/// channel for an organization, and deactivating an organization (its members are signed out and refused, the others are not).
/// The API connects as <c>app_user</c>, as in production, and the Site admin signs in with their own cookie.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class SiteChannelsApiTests(PostgresFixture postgres) : AuthApiTestBase(postgres)
{
    private const string AdminPassword = "the site admin password";

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];

    private readonly Pbkdf2PasswordHasher _hasher = new(iterations: 1_000);

    private sealed record SiteSession(Guid UserId, string AccessToken);

    private async Task<SiteSession> SiteLoginAsync(string fullName = "Root Admin")
    {
        var email = $"root-{Guid.NewGuid():N}@example.com";
        var userId = Guid.NewGuid();
        await OwnerExecuteAsync(
            "INSERT INTO users (id, email, password_hash, full_name) VALUES (@userId, @email, @hash, @fullName); INSERT INTO site_admins (user_id) VALUES (@userId)",
            new { userId, email, hash = _hasher.Hash(AdminPassword), fullName });
        var response = await Host.PostAsync("/v1/site/login", new { email, password = AdminPassword });
        response.EnsureSuccessStatusCode();
        return new SiteSession(userId, ApiTestHost.SiteCookieValue(response)!);
    }

    private Task<HttpResponseMessage> GetAsync(string path, SiteSession session) =>
        Host.GetAsync(path, tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName);

    private Task<HttpResponseMessage> PutAsync(string path, object body, SiteSession session) =>
        Host.PutAsync(path, body, tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName);

    private Task<HttpResponseMessage> CreateChannelAsync(
        SiteSession session, string? name, string? description = null, byte[]? icon = null, string iconType = "image/png") =>
        Host.PostAsync(
            "/v1/site/channels", ApiTestHost.ChannelForm(name, description, icon, iconType), tokenCookie: session.AccessToken, cookieName: ApiTestHost.SiteCookieName);

    private async Task<List<JsonElement>> ChannelsAsync(SiteSession session)
    {
        var response = await GetAsync("/v1/site/channels", session);
        AssertStatus(HttpStatusCode.OK, response);
        return (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();
    }

    private async Task<Guid> ChannelIdAsync(SiteSession session, string name) =>
        (await ChannelsAsync(session)).Single(c => c.GetProperty("name").GetString() == name).GetProperty("id").GetGuid();

    private async Task<JsonElement> OrganizationAsync(SiteSession session, Guid agencyId)
    {
        var response = await GetAsync("/v1/site/agencies?page=1&pageSize=100", session);
        AssertStatus(HttpStatusCode.OK, response);
        return (await ApiTestHost.ReadJsonAsync(response)).GetProperty("items").EnumerateArray().Single(o => o.GetProperty("id").GetGuid() == agencyId);
    }

    // --- The list and creating a channel -----------------------------------------------------

    [Fact]
    public async Task TheList_HasTheSevenBuiltInChannels_InOrder_WithNoCreatorAndNoOrganizations()
    {
        var session = await SiteLoginAsync();

        var response = await GetAsync("/v1/site/channels", session);

        AssertStatus(HttpStatusCode.OK, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var channels = (await ApiTestHost.ReadJsonAsync(response)).EnumerateArray().ToList();
        Assert.Equal(["Instagram", "Facebook", "YouTube", "Email", "SMS", "WhatsApp", "LinkedIn"], channels.Select(c => c.GetProperty("name").GetString()));
        Assert.Equal(["instagram", "facebook", "youtube", "email", "sms", "whatsapp", "linkedin"], channels.Select(c => c.GetProperty("iconKey").GetString()));
        Assert.All(channels, c =>
        {
            Assert.False(c.GetProperty("hasIconImage").GetBoolean());
            Assert.Equal(JsonValueKind.Null, c.GetProperty("createdByName").ValueKind);
            Assert.Equal(0, c.GetProperty("organizationCount").GetInt32());
            Assert.NotEmpty(c.GetProperty("description").GetString()!);
        });
        Assert.Equal(
            ["id", "name", "description", "iconKey", "hasIconImage", "createdByName", "createdAt", "organizationCount"],
            channels[0].EnumerateObject().Select(p => p.Name));
    }

    [Fact]
    public async Task CreatingAChannel_WithAnImage_Returns201_ListsIt_AndServesTheImageBackByteForByte()
    {
        var session = await SiteLoginAsync("Rama Admin");

        var response = await CreateChannelAsync(session, "  Telegram ", "Fast chat", Png);

        AssertStatus(HttpStatusCode.Created, response);
        Assert.True(response.Headers.CacheControl?.NoStore);
        var created = await ApiTestHost.ReadJsonAsync(response);
        var id = created.GetProperty("id").GetGuid();
        Assert.Equal($"/v1/site/channels/{id}", response.Headers.Location?.ToString());
        Assert.Equal("Telegram", created.GetProperty("name").GetString());
        Assert.Equal("Fast chat", created.GetProperty("description").GetString());
        Assert.True(created.GetProperty("hasIconImage").GetBoolean());
        Assert.Equal(JsonValueKind.Null, created.GetProperty("iconKey").ValueKind);
        Assert.Equal("Rama Admin", created.GetProperty("createdByName").GetString());

        var listed = (await ChannelsAsync(session)).Last();
        Assert.Equal(id, listed.GetProperty("id").GetGuid());

        var icon = await GetAsync($"/v1/site/channels/{id}/icon", session);
        AssertStatus(HttpStatusCode.OK, icon);
        Assert.Equal("image/png", icon.Content.Headers.ContentType?.MediaType);
        Assert.Equal(Png, await icon.Content.ReadAsByteArrayAsync());
        Assert.Equal("nosniff", icon.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.True(icon.Headers.CacheControl?.Private);
        Assert.Equal(TimeSpan.FromHours(1), icon.Headers.CacheControl?.MaxAge);
    }

    [Fact]
    public async Task TheStoredType_IsTheOneFoundInTheFile_NotTheOneTheBrowserClaimed()
    {
        var session = await SiteLoginAsync();
        var jpeg = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10, 1, 2, 3 };

        // A browser that names the type wrongly is refused: the file is not what it says it is.
        AssertStatus(HttpStatusCode.BadRequest, await CreateChannelAsync(session, "Wrong claim", null, jpeg, "image/png"));

        var created = await CreateChannelAsync(session, "Honest", null, jpeg, "image/jpeg");
        AssertStatus(HttpStatusCode.Created, created);
        var id = (await ApiTestHost.ReadJsonAsync(created)).GetProperty("id").GetGuid();
        Assert.Equal("image/jpeg", (await GetAsync($"/v1/site/channels/{id}/icon", session)).Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task CreatingAChannel_WithNoIcon_GetsTheDefaultGlyph_AndHasNoImageToServe()
    {
        var session = await SiteLoginAsync();

        var response = await CreateChannelAsync(session, "Telegram");

        AssertStatus(HttpStatusCode.Created, response);
        var created = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal(ChannelIconRules.DefaultGlyph, created.GetProperty("iconKey").GetString());
        Assert.False(created.GetProperty("hasIconImage").GetBoolean());
        Assert.Equal(string.Empty, created.GetProperty("description").GetString());

        var icon = await GetAsync($"/v1/site/channels/{created.GetProperty("id").GetGuid()}/icon", session);
        AssertStatus(HttpStatusCode.NotFound, icon);
        Assert.Equal("channel_not_found", await CodeOfAsync(icon));
    }

    [Fact]
    public async Task AnIconOfABuiltInChannelOrAnUnknownChannel_IsNotFound()
    {
        var session = await SiteLoginAsync();
        var email = await ChannelIdAsync(session, "Email");

        AssertStatus(HttpStatusCode.NotFound, await GetAsync($"/v1/site/channels/{email}/icon", session));
        AssertStatus(HttpStatusCode.NotFound, await GetAsync($"/v1/site/channels/{Guid.NewGuid()}/icon", session));
    }

    [Fact]
    public async Task ATakenName_IsAConflict_WhateverTheLetterCase_AndNothingIsAdded()
    {
        var session = await SiteLoginAsync();
        AssertStatus(HttpStatusCode.Created, await CreateChannelAsync(session, "Telegram"));

        foreach (var name in new[] { "Telegram", "telegram", "  TELEGRAM ", "email", "Instagram" })
        {
            var response = await CreateChannelAsync(session, name);

            AssertStatus(HttpStatusCode.Conflict, response);
            Assert.Equal("channel_exists", await CodeOfAsync(response));
        }

        Assert.Equal(8, (await ChannelsAsync(session)).Count);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"></svg>", "image/svg+xml")]
    [InlineData("GIF89a....", "image/gif")]
    [InlineData("just some text", "text/plain")]
    [InlineData("<svg onload=\"alert(1)\"></svg>", "image/png")]
    public async Task AnIconThatIsNotAPngJpegOrWebP_IsABadRequest_AndNothingIsAdded(string content, string claimedType)
    {
        var session = await SiteLoginAsync();

        var response = await CreateChannelAsync(session, "Telegram", null, System.Text.Encoding.UTF8.GetBytes(content), claimedType);

        AssertStatus(HttpStatusCode.BadRequest, response);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("icon", out _));
        Assert.Equal(7, (await ChannelsAsync(session)).Count);
    }

    [Fact]
    public async Task AnImageOfExactly100KB_IsAccepted_AndOneByteMoreIsNot()
    {
        var session = await SiteLoginAsync();
        byte[] Image(int length)
        {
            var bytes = new byte[length];
            Png.CopyTo(bytes, 0);
            return bytes;
        }

        AssertStatus(HttpStatusCode.Created, await CreateChannelAsync(session, "Exactly", null, Image(100 * 1024)));
        AssertStatus(HttpStatusCode.BadRequest, await CreateChannelAsync(session, "Too big", null, Image(100 * 1024 + 1)));
    }

    // --- One organization's channels ---------------------------------------------------------

    [Fact]
    public async Task AChannelIsEnabledAndDisabledForAnOrganization_ForEveryKind_AndTheCountsFollow()
    {
        var company = await Host.RegisterAsync(agencyName: "Northwind", businessModel: "company");
        var agency = await Host.RegisterAsync(agencyName: "Acme Agency", businessModel: "agency");
        var creator = await Host.RegisterAsync(agencyName: "Cleo Makes", businessModel: "creator");
        var session = await SiteLoginAsync();
        var email = await ChannelIdAsync(session, "Email");

        foreach (var org in new[] { company, agency, creator })
        {
            var before = await GetAsync($"/v1/site/agencies/{org.AgencyId}/channels", session);
            AssertStatus(HttpStatusCode.OK, before);
            Assert.True(before.Headers.CacheControl?.NoStore);
            var channels = (await ApiTestHost.ReadJsonAsync(before)).EnumerateArray().ToList();
            Assert.Equal(7, channels.Count);
            Assert.All(channels, c => Assert.False(c.GetProperty("isEnabled").GetBoolean()));
            Assert.Equal(["id", "name", "description", "iconKey", "hasIconImage", "isEnabled"], channels[0].EnumerateObject().Select(p => p.Name));

            AssertStatus(HttpStatusCode.NoContent, await PutAsync($"/v1/site/agencies/{org.AgencyId}/channels/{email}", new { enabled = true }, session));
            AssertStatus(HttpStatusCode.NoContent, await PutAsync($"/v1/site/agencies/{org.AgencyId}/channels/{email}", new { enabled = true }, session)); // repeating is harmless

            var after = (await ApiTestHost.ReadJsonAsync(await GetAsync($"/v1/site/agencies/{org.AgencyId}/channels", session))).EnumerateArray().ToList();
            Assert.Equal(["Email"], after.Where(c => c.GetProperty("isEnabled").GetBoolean()).Select(c => c.GetProperty("name").GetString()));
            Assert.Equal(1, (await OrganizationAsync(session, org.AgencyId)).GetProperty("channels").GetInt32());
        }

        Assert.Equal(3, (await ChannelsAsync(session)).Single(c => c.GetProperty("name").GetString() == "Email").GetProperty("organizationCount").GetInt32());

        AssertStatus(HttpStatusCode.NoContent, await PutAsync($"/v1/site/agencies/{agency.AgencyId}/channels/{email}", new { enabled = false }, session));
        AssertStatus(HttpStatusCode.NoContent, await PutAsync($"/v1/site/agencies/{agency.AgencyId}/channels/{email}", new { enabled = false }, session));

        Assert.Equal(0, (await OrganizationAsync(session, agency.AgencyId)).GetProperty("channels").GetInt32());
        Assert.Equal(1, (await OrganizationAsync(session, company.AgencyId)).GetProperty("channels").GetInt32());
        Assert.Equal(2, (await ChannelsAsync(session)).Single(c => c.GetProperty("name").GetString() == "Email").GetProperty("organizationCount").GetInt32());
    }

    [Fact]
    public async Task AChannelTheAdminCreated_CanBeEnabledLikeABuiltInOne()
    {
        var org = await Host.RegisterAsync(businessModel: "company");
        var session = await SiteLoginAsync();
        var created = await ApiTestHost.ReadJsonAsync(await CreateChannelAsync(session, "Telegram", "Fast chat", Png));
        var id = created.GetProperty("id").GetGuid();

        AssertStatus(HttpStatusCode.NoContent, await PutAsync($"/v1/site/agencies/{org.AgencyId}/channels/{id}", new { enabled = true }, session));

        var forOrg = (await ApiTestHost.ReadJsonAsync(await GetAsync($"/v1/site/agencies/{org.AgencyId}/channels", session))).EnumerateArray().ToList();
        var telegram = forOrg.Single(c => c.GetProperty("name").GetString() == "Telegram");
        Assert.True(telegram.GetProperty("isEnabled").GetBoolean());
        Assert.True(telegram.GetProperty("hasIconImage").GetBoolean());
    }

    [Fact]
    public async Task AnUnknownOrganizationOrChannel_IsNotFound_WithItsOwnCode()
    {
        var org = await Host.RegisterAsync();
        var session = await SiteLoginAsync();
        var email = await ChannelIdAsync(session, "Email");

        var noOrganization = await GetAsync($"/v1/site/agencies/{Guid.NewGuid()}/channels", session);
        var noOrganizationToSet = await PutAsync($"/v1/site/agencies/{Guid.NewGuid()}/channels/{email}", new { enabled = true }, session);
        var noChannel = await PutAsync($"/v1/site/agencies/{org.AgencyId}/channels/{Guid.NewGuid()}", new { enabled = true }, session);
        var noOrganizationToDeactivate = await PutAsync($"/v1/site/agencies/{Guid.NewGuid()}/status", new { isActive = false }, session);

        foreach (var response in new[] { noOrganization, noOrganizationToSet, noOrganizationToDeactivate })
        {
            AssertStatus(HttpStatusCode.NotFound, response);
            Assert.Equal("organization_not_found", await CodeOfAsync(response));
        }

        AssertStatus(HttpStatusCode.NotFound, noChannel);
        Assert.Equal("channel_not_found", await CodeOfAsync(noChannel));
    }

    // --- Deactivating an organization --------------------------------------------------------

    [Fact]
    public async Task Deactivating_SignsEveryMemberOutAtOnce_RefusesThemWithAClearCode_AndLeavesOtherOrganizationsAlone()
    {
        var owner = await Host.RegisterAsync(agencyName: "Doomed", businessModel: "company");
        var bystander = await Host.RegisterAsync(agencyName: "Bystander", businessModel: "company");
        var session = await SiteLoginAsync();
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: owner.AccessToken));

        AssertStatus(HttpStatusCode.NoContent, await PutAsync($"/v1/site/agencies/{owner.AgencyId}/status", new { isActive = false }, session));

        // The token they held is void at once ...
        var old = await Host.GetAsync("/v1/workspaces", tokenCookie: owner.AccessToken);
        AssertStatus(HttpStatusCode.Unauthorized, old);
        Assert.Equal("session_ended", await CodeOfAsync(old));
        // ... and signing in is refused, only after the password is right.
        var login = await Host.PostAsync("/v1/auth/login", new { email = owner.Email, password = owner.Password });
        AssertStatus(HttpStatusCode.Forbidden, login);
        Assert.Equal("organization_deactivated", await CodeOfAsync(login));
        Assert.Null(ApiTestHost.MemberCookieHeader(login));
        var wrongPassword = await Host.PostAsync("/v1/auth/login", new { email = owner.Email, password = "definitely not the password" });
        AssertStatus(HttpStatusCode.Unauthorized, wrongPassword);
        Assert.Equal("invalid_credentials", await CodeOfAsync(wrongPassword));
        // Another organization is not touched.
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: bystander.AccessToken));
        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/login", new { email = bystander.Email, password = bystander.Password }));

        // The list says so, and nothing was deleted.
        Assert.False((await OrganizationAsync(session, owner.AgencyId)).GetProperty("isActive").GetBoolean());
        Assert.True((await OrganizationAsync(session, bystander.AgencyId)).GetProperty("isActive").GetBoolean());
        Assert.Equal(1, await OwnerQueryAsync<long>("SELECT count(*) FROM agency_members WHERE agency_id = @id", new { id = owner.AgencyId }));
    }

    [Fact]
    public async Task Activating_LetsThePeopleSignInAgain_ButNeverRevivesATokenFromBeforeTheDeactivation()
    {
        var owner = await Host.RegisterAsync(agencyName: "Doomed", businessModel: "company");
        var session = await SiteLoginAsync();
        await PutAsync($"/v1/site/agencies/{owner.AgencyId}/status", new { isActive = false }, session);

        AssertStatus(HttpStatusCode.NoContent, await PutAsync($"/v1/site/agencies/{owner.AgencyId}/status", new { isActive = true }, session));

        Assert.True((await OrganizationAsync(session, owner.AgencyId)).GetProperty("isActive").GetBoolean());
        // A person who signs in within the second of the change is refused once, by the same rule as every other session end.
        await Task.Delay(1200);
        var login = await Host.PostAsync("/v1/auth/login", new { email = owner.Email, password = owner.Password });
        AssertStatus(HttpStatusCode.OK, login);
        AssertStatus(HttpStatusCode.OK, await Host.GetAsync("/v1/workspaces", tokenCookie: ApiTestHost.MemberCookieValue(login)));
        AssertStatus(HttpStatusCode.Unauthorized, await Host.GetAsync("/v1/workspaces", tokenCookie: owner.AccessToken));
    }

    [Fact]
    public async Task APersonInTwoOrganizations_SignsInToTheOneThatIsActive()
    {
        var person = await Host.RegisterAsync(agencyName: "First", businessModel: "company");
        var secondId = await AddMembershipAsync(person, "contentwriter", "Second agency");
        var session = await SiteLoginAsync();

        // The most recently used organization is the default: deactivate that one, and the other is where they land.
        await PutAsync($"/v1/site/agencies/{secondId}/status", new { isActive = false }, session);

        var login = await Host.PostAsync("/v1/auth/login", new { email = person.Email, password = person.Password });
        AssertStatus(HttpStatusCode.OK, login);
        var me = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/auth/me", tokenCookie: ApiTestHost.MemberCookieValue(login)));
        Assert.Equal(person.AgencyId, me.GetProperty("agency").GetProperty("id").GetGuid());
        Assert.Equal([person.AgencyId], me.GetProperty("memberships").EnumerateArray().Select(m => m.GetProperty("agencyId").GetGuid()));

        // Asking for the deactivated one by name says why.
        var named = await Host.PostAsync("/v1/auth/login", new { email = person.Email, password = person.Password, agencyId = secondId });
        AssertStatus(HttpStatusCode.Forbidden, named);
        Assert.Equal("organization_deactivated", await CodeOfAsync(named));
    }

    [Fact]
    public async Task AnInvitationIntoADeactivatedOrganization_CannotBePreviewedOrAccepted_UntilItIsActivated()
    {
        var admin = await Host.RegisterAsync(agencyName: "Doomed", businessModel: "company");
        var session = await SiteLoginAsync();
        var email = $"employee-{Guid.NewGuid():N}@example.com";
        var invited = await Host.PostAsync("/v1/members/invitations", new { fullName = "Test Person", email, role = "contentwriter" }, tokenCookie: admin.AccessToken);
        AssertStatus(HttpStatusCode.Created, invited);
        var token = (await ApiTestHost.ReadJsonAsync(invited)).GetProperty("token").GetString()!;

        await PutAsync($"/v1/site/agencies/{admin.AgencyId}/status", new { isActive = false }, session);

        var preview = await Host.PostAsync("/v1/auth/invitations/preview", new { token });
        var accept = await Host.PostAsync("/v1/auth/invitations/accept", new { token, fullName = "Eve", password = "employee password 123" });
        AssertStatus(HttpStatusCode.NotFound, preview);
        AssertStatus(HttpStatusCode.NotFound, accept);
        Assert.Equal("invitation_invalid", await CodeOfAsync(preview));
        Assert.Equal("invitation_invalid", await CodeOfAsync(accept));
        Assert.Equal(0, await OwnerQueryAsync<long>("SELECT count(*) FROM users WHERE email = @email", new { email }));

        await PutAsync($"/v1/site/agencies/{admin.AgencyId}/status", new { isActive = true }, session);

        AssertStatus(HttpStatusCode.OK, await Host.PostAsync("/v1/auth/invitations/preview", new { token }));
        AssertStatus(HttpStatusCode.Created, await Host.PostAsync("/v1/auth/invitations/accept", new { token, fullName = "Eve", password = "employee password 123" }));
    }

    [Fact]
    public async Task AnOrganizationsOwnMembers_CannotDeactivateOrActivateAnything_ThroughTheSiteRoutes()
    {
        var owner = await Host.RegisterAsync(businessModel: "company");

        // A member cookie is never read by a Site admin route; as a header the token is judged for what it is.
        AssertStatus(HttpStatusCode.Unauthorized, await Host.PutAsync($"/v1/site/agencies/{owner.AgencyId}/status", new { isActive = false }, tokenCookie: owner.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.PutAsync($"/v1/site/agencies/{owner.AgencyId}/status", new { isActive = false }, owner.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.PutAsync($"/v1/site/agencies/{owner.AgencyId}/channels/{Guid.NewGuid()}", new { enabled = true }, owner.AccessToken));
        AssertStatus(HttpStatusCode.Forbidden, await Host.GetAsync("/v1/site/channels", owner.AccessToken));

        Assert.True(await OwnerQueryAsync<bool>("SELECT is_active FROM agencies WHERE id = @id", new { id = owner.AgencyId }));
    }

    [Fact]
    public async Task AfterTheUserIsNoLongerASiteAdmin_TheDatabaseRefusesEveryChannelRouteAtOnce_EvenWithAValidToken()
    {
        var org = await Host.RegisterAsync();
        var session = await SiteLoginAsync();
        var email = await ChannelIdAsync(session, "Email");
        await OwnerExecuteAsync("DELETE FROM site_admins WHERE user_id = @userId", new { userId = session.UserId });

        var responses = new[]
        {
            await GetAsync("/v1/site/channels", session),
            await CreateChannelAsync(session, "Telegram"),
            await GetAsync($"/v1/site/channels/{email}/icon", session),
            await GetAsync($"/v1/site/agencies/{org.AgencyId}/channels", session),
            await PutAsync($"/v1/site/agencies/{org.AgencyId}/channels/{email}", new { enabled = true }, session),
            await PutAsync($"/v1/site/agencies/{org.AgencyId}/status", new { isActive = false }, session)
        };

        foreach (var response in responses)
        {
            AssertStatus(HttpStatusCode.Forbidden, response);
            Assert.Equal("not_site_admin", await CodeOfAsync(response));
        }

        Assert.True(await OwnerQueryAsync<bool>("SELECT is_active FROM agencies WHERE id = @id", new { id = org.AgencyId }));
        Assert.Equal(7, await OwnerQueryAsync<long>("SELECT count(*) FROM channels"));
    }

    [Fact]
    public async Task WhatTheChannelRoutesLog_IsIdsOnly_NeverTheImageTheTextOrTheAddressOfAnyone()
    {
        var owner = await Host.RegisterAsync(agencyName: "Doomed Organization Name", businessModel: "company");
        var session = await SiteLoginAsync("Rama Admin");
        var created = await ApiTestHost.ReadJsonAsync(await CreateChannelAsync(session, "Telegram Secret Name", "A description nobody should log", Png));
        var channelId = created.GetProperty("id").GetGuid();
        await PutAsync($"/v1/site/agencies/{owner.AgencyId}/channels/{channelId}", new { enabled = true }, session);
        await PutAsync($"/v1/site/agencies/{owner.AgencyId}/status", new { isActive = false }, session);
        await CreateChannelAsync(session, "Telegram Secret Name"); // a refused request is logged too

        var everything = string.Join('\n', _logs.Messages);

        Assert.Contains(channelId.ToString(), everything); // the channel is logged by its id ...
        Assert.Contains(owner.AgencyId.ToString(), everything); // ... and the organization by its id
        foreach (var secret in new[]
                 {
                     "A description nobody should log", "Rama Admin", owner.Email, owner.Password, session.AccessToken,
                     Convert.ToBase64String(Png), Convert.ToHexString(Png)
                 })
        {
            Assert.DoesNotContain(secret, everything);
        }
    }

    private readonly CapturingLoggerProvider _logs = new();

    protected override void ConfigureServices(IServiceCollection services) =>
        services.AddLogging(builder => builder.AddProvider(_logs));
}
