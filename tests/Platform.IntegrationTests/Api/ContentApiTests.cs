using System.Net;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Platform.Content.Interfaces;
using Platform.Helpers.Constants;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Content Studio end to end over a real database, connecting as <c>app_user</c> as in production: saving and reading a post with
/// everything that hangs off it, the list and its filters, optimistic concurrency, the status rules, who may do what, and that no
/// organization sees another's content. The publishing worker is switched off in the host and called by hand, so each test decides
/// when it looks (<see cref="IPublishingService"/>), with the development fake publisher standing in for every platform.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class ContentApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiTestHost Host { get; set; } = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        Host = await ApiTestHost.StartWithDatabaseAsync(postgres, configure: (settings, _) => settings["Content:Publishing:UseFakePublisher"] = "true");
    }

    public async Task DisposeAsync() => await Host.DisposeAsync();

    private static readonly CancellationToken Ct = CancellationToken.None;

    private static JsonElement Doc(string text) =>
        JsonDocument.Parse($$"""{"type":"doc","content":[{"type":"paragraph","content":[{"type":"text","text":{{JsonSerializer.Serialize(text)}} }]}]}""").RootElement.Clone();

    private sealed record Member(Session Session, Guid WorkspaceId)
    {
        public string Token => Session.AccessToken;
    }

    private async Task<Member> OrganizationAsync(string model = "company", params string[] channels)
    {
        var session = await Host.RegisterAsync(businessModel: model);
        var workspaces = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/workspaces", session.AccessToken));
        await using var owner = await Host.Database.OpenOwnerAsync();
        foreach (var channel in channels.Length == 0 ? ["Instagram", "LinkedIn"] : channels)
        {
            await owner.ExecuteAsync(
                "INSERT INTO organization_channels (agency_id, channel_id) SELECT @agencyId, id FROM channels WHERE name = @channel ON CONFLICT DO NOTHING",
                new { agencyId = session.AgencyId, channel });
        }

        return new Member(session, workspaces[0].GetProperty("id").GetGuid());
    }

    private async Task<JsonElement> CreateAsync(Member who, string text = "Hello world", string? title = "Dream Bigger", params string[] platforms)
    {
        var response = await Host.PostAsync("/v1/content/posts", new
        {
            workspaceId = who.WorkspaceId,
            contentType = "social_post",
            title,
            bodyDoc = Doc(text),
            hashtags = new[] { "#JustMakeIt", "creativity", "justmakeit" },
            tags = new[] { "Motivation" },
            platforms = (platforms.Length == 0 ? ["instagram"] : platforms).Select(p => new { platform = p }).ToArray(),
            poll = new { question = "Pick one?", options = new[] { "Yes", "No" } },
            ctas = new[] { new { buttonText = "Learn more", targetUrl = "https://example.com" } },
            location = new { name = "Chennai", latitude = 13.0827, longitude = 80.2707 }
        }, who.Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private async Task<JsonElement> GetAsync(Member who, JsonElement post) =>
        await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/content/posts/{post.GetProperty("id").GetGuid()}", who.Token));

    private async Task<HttpResponseMessage> PostAsync(Member who, JsonElement post, string action, object? body = null) =>
        await Host.PostAsync($"/v1/content/posts/{post.GetProperty("id").GetGuid()}/{action}", body, who.Token);

    private async Task RunTheWorkerAsync()
    {
        await using var scope = Host.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IPublishingService>().ProcessDueAsync(Ct);
    }

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    // --- Saving and reading ----------------------------------------------------------------------------

    [Fact]
    public async Task APostIsSavedWithEverythingThatHangsOffIt_AndReadBackTheSame()
    {
        var who = await OrganizationAsync();

        var created = await CreateAsync(who);
        var read = await GetAsync(who, created);

        Assert.Equal("draft", read.GetProperty("status").GetString());
        Assert.Equal("Hello world", read.GetProperty("bodyText").GetString());
        Assert.Equal(JsonValueKind.Object, read.GetProperty("bodyDoc").ValueKind);
        Assert.Equal(["JustMakeIt", "creativity"], read.GetProperty("hashtags").EnumerateArray().Select(h => h.GetString()));
        Assert.Equal("Motivation", read.GetProperty("tags")[0].GetProperty("name").GetString());
        Assert.Equal("pending", read.GetProperty("platforms")[0].GetProperty("publishStatus").GetString());
        Assert.Equal(["Yes", "No"], read.GetProperty("poll").GetProperty("options").EnumerateArray().Select(o => o.GetString()));
        Assert.Equal("Chennai", read.GetProperty("location").GetProperty("name").GetString());
        Assert.Equal(1, await RevisionCountAsync(read));
    }

    private async Task<long> RevisionCountAsync(JsonElement post)
    {
        await using var owner = await Host.Database.OpenOwnerAsync();
        return await owner.QuerySingleAsync<long>("SELECT count(*) FROM content_revisions WHERE post_id = @id", new { id = post.GetProperty("id").GetGuid() });
    }

    [Fact]
    public async Task AnEdit_NeedsTheVersionThePageLoaded_AndWritesARevision()
    {
        var who = await OrganizationAsync();
        var created = await CreateAsync(who);
        var id = created.GetProperty("id").GetGuid();
        var loaded = created.GetProperty("updatedAt");

        var edit = await Host.PutAsync($"/v1/content/posts/{id}", new { expectedUpdatedAt = loaded, title = "Dream Bigger", bodyDoc = Doc("Edited"), platforms = new[] { new { platform = "instagram" } } }, who.Token);
        var stale = await Host.PutAsync($"/v1/content/posts/{id}", new { expectedUpdatedAt = loaded, title = "x", bodyDoc = Doc("Too late"), platforms = new[] { new { platform = "instagram" } } }, who.Token);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        Assert.Equal("content_changed", Code(await ApiTestHost.ReadJsonAsync(stale)));
        Assert.Equal(2, await RevisionCountAsync(created));
        Assert.Equal("Edited", (await GetAsync(who, created)).GetProperty("bodyText").GetString());
    }

    [Fact]
    public async Task ABadRequest_IsAProblemWithAMessagePerField_AndAPlatformThatIsNotEnabledIsRefused()
    {
        var who = await OrganizationAsync();

        var bad = await Host.PostAsync("/v1/content/posts", new { workspaceId = who.WorkspaceId, contentType = "podcast" }, who.Token);
        var tiktok = await Host.PostAsync(
            "/v1/content/posts", new { workspaceId = who.WorkspaceId, contentType = "social_post", title = "x", platforms = new[] { new { platform = "tiktok" } } }, who.Token);

        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(bad)).GetProperty("errors").TryGetProperty("contentType", out _));
        Assert.Equal("platform_not_enabled", Code(await ApiTestHost.ReadJsonAsync(tiktok)));
    }

    // --- The list ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task TheList_FiltersBySearchPlatformAndStatus_AndKeepsTheTabCounts()
    {
        var who = await OrganizationAsync();
        await CreateAsync(who, "Work smarter", "Productivity Tips", "linkedin");
        await CreateAsync(who, "Hello", "Dream Bigger", "instagram");

        async Task<JsonElement> List(string query) => await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/content/posts" + query, who.Token));

        Assert.Equal(2, (await List(string.Empty)).GetProperty("totalCount").GetInt64());
        Assert.Equal(1, (await List("?q=bigg")).GetProperty("totalCount").GetInt64());
        Assert.Equal(1, (await List("?platform=linkedin")).GetProperty("totalCount").GetInt64());
        Assert.Equal(0, (await List("?status=published")).GetProperty("totalCount").GetInt64());
        var blogTab = await List("?type=blog_post");
        Assert.Equal(0, blogTab.GetProperty("totalCount").GetInt64());
        Assert.Equal(2, blogTab.GetProperty("typeCounts").GetProperty("all").GetInt64());
        Assert.Equal(HttpStatusCode.BadRequest, (await Host.GetAsync("/v1/content/posts?type=podcast", who.Token)).StatusCode);
    }

    // --- Isolation --------------------------------------------------------------------------------------------

    [Fact]
    public async Task AnotherOrganization_CannotReadChangeScheduleDeleteOrListAPost()
    {
        var mine = await OrganizationAsync();
        var theirs = await OrganizationAsync();
        var post = await CreateAsync(mine);
        var id = post.GetProperty("id").GetGuid();

        Assert.Equal(HttpStatusCode.NotFound, (await Host.GetAsync($"/v1/content/posts/{id}", theirs.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.GetAsync($"/v1/content/posts/{id}/revisions", theirs.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(theirs, post, "archive")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(theirs, post, "duplicate")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await PostAsync(theirs, post, "schedule", new { scheduledAt = DateTimeOffset.UtcNow.AddHours(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.DeleteAsync($"/v1/content/posts/{id}", theirs.Token)).StatusCode);
        Assert.Equal(0, (await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/content/posts", theirs.Token))).GetProperty("totalCount").GetInt64());
        Assert.Equal(0, (await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/content/tags", theirs.Token))).GetArrayLength());
        var intoTheirWorkspace = await Host.PostAsync("/v1/content/posts", new { workspaceId = mine.WorkspaceId, contentType = "social_post", title = "x" }, theirs.Token);
        Assert.Equal("workspace_not_found", Code(await ApiTestHost.ReadJsonAsync(intoTheirWorkspace)));
    }

    [Fact]
    public async Task APlatformEnabledForOneOrganization_IsNotEnabledForAnother()
    {
        var withInstagram = await OrganizationAsync("company", "Instagram");
        var without = await OrganizationAsync("company", "LinkedIn");

        var rules = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/content/platform-rules", without.Token));

        Assert.Equal(["linkedin"], rules.EnumerateArray().Where(r => r.GetProperty("enabled").GetBoolean()).Select(r => r.GetProperty("platform").GetString()));
        Assert.Equal(HttpStatusCode.BadRequest, (await Host.PostAsync("/v1/content/posts",
            new { workspaceId = without.WorkspaceId, contentType = "social_post", title = "x", platforms = new[] { new { platform = "instagram" } } }, without.Token)).StatusCode);
        Assert.NotNull(withInstagram);
    }

    [Fact]
    public async Task SignedOut_EverythingIsRefused()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, (await Host.GetAsync("/v1/content/posts")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Host.GetAsync("/v1/content/capabilities")).StatusCode);
    }

    // --- Scheduling and the worker ---------------------------------------------------------------------------

    [Fact]
    public async Task AScheduledPost_CanBeRescheduledEditedAndTakenBack_ButNotArchivedOrDeleted()
    {
        var who = await OrganizationAsync();
        var post = await CreateAsync(who);

        var scheduled = await PostAsync(who, post, "schedule", new { scheduledAt = DateTimeOffset.UtcNow.AddHours(2) });
        var soon = await PostAsync(who, post, "schedule", new { scheduledAt = DateTimeOffset.UtcNow.AddSeconds(10) });
        var rescheduled = await PostAsync(who, post, "schedule", new { scheduledAt = DateTimeOffset.UtcNow.AddHours(3) });

        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        Assert.Equal("scheduled", (await ApiTestHost.ReadJsonAsync(scheduled)).GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.BadRequest, soon.StatusCode);
        Assert.Equal(HttpStatusCode.OK, rescheduled.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(who, post, "archive")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await Host.DeleteAsync($"/v1/content/posts/{post.GetProperty("id").GetGuid()}", who.Token)).StatusCode);

        var back = await ApiTestHost.ReadJsonAsync(await PostAsync(who, post, "unschedule"));
        Assert.Equal("draft", back.GetProperty("status").GetString());
        Assert.Equal("pending", back.GetProperty("platforms")[0].GetProperty("publishStatus").GetString());
    }

    [Fact]
    public async Task PublishNow_IsDeliveredByTheWorker_ThroughThePlatformsPublisher_AndThePostIsThenArchivedAndRestored()
    {
        var who = await OrganizationAsync();
        var post = await CreateAsync(who);

        var now = await ApiTestHost.ReadJsonAsync(await PostAsync(who, post, "publish-now"));
        await RunTheWorkerAsync();
        var published = await GetAsync(who, post);

        Assert.Equal("publishing", now.GetProperty("status").GetString());
        Assert.Equal("published", published.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.String, published.GetProperty("publishedAt").ValueKind);
        var target = published.GetProperty("platforms")[0];
        Assert.Equal("published", target.GetProperty("publishStatus").GetString());
        Assert.StartsWith("https://example.com/fake/instagram/", target.GetProperty("externalPostUrl").GetString());

        var edit = await Host.PutAsync($"/v1/content/posts/{post.GetProperty("id").GetGuid()}", new { expectedUpdatedAt = published.GetProperty("updatedAt"), title = "x", bodyDoc = Doc("x") }, who.Token);
        Assert.Equal("post_not_editable", Code(await ApiTestHost.ReadJsonAsync(edit)));
        Assert.Equal("post_not_deletable", Code(await ApiTestHost.ReadJsonAsync(await Host.DeleteAsync($"/v1/content/posts/{post.GetProperty("id").GetGuid()}", who.Token))));
        Assert.Equal("archived", (await ApiTestHost.ReadJsonAsync(await PostAsync(who, post, "archive"))).GetProperty("status").GetString());
        Assert.Equal("published", (await ApiTestHost.ReadJsonAsync(await PostAsync(who, post, "restore"))).GetProperty("status").GetString());
    }

    [Fact]
    public async Task APublisherThatRefuses_FailsThePost_WithAMessage_AndAFailedPostCanBeFixedAndSentAgain()
    {
        var who = await OrganizationAsync();
        var post = await CreateAsync(who, "this will [fail] for good");

        await PostAsync(who, post, "publish-now");
        await RunTheWorkerAsync();
        var failed = await GetAsync(who, post);

        Assert.Equal("failed", failed.GetProperty("status").GetString());
        Assert.Equal("The fake publisher was asked to fail.", failed.GetProperty("platforms")[0].GetProperty("lastError").GetString());
        Assert.Equal(1, failed.GetProperty("platforms")[0].GetProperty("retryCount").GetInt32());

        var fix = await Host.PutAsync($"/v1/content/posts/{post.GetProperty("id").GetGuid()}",
            new { expectedUpdatedAt = failed.GetProperty("updatedAt"), bodyDoc = Doc("a better text"), platforms = new[] { new { platform = "instagram" } } }, who.Token);
        Assert.Equal(HttpStatusCode.OK, fix.StatusCode);
        await PostAsync(who, post, "publish-now");
        await RunTheWorkerAsync();
        Assert.Equal("published", (await GetAsync(who, post)).GetProperty("status").GetString());
    }

    [Fact]
    public async Task AFailureWorthRetrying_IsScheduledAgainAndCounted_AndTheMarkerIsTheOnlyWayTheFakeFails()
    {
        var who = await OrganizationAsync();
        var post = await CreateAsync(who, "try [retry] later");

        await PostAsync(who, post, "publish-now");
        await RunTheWorkerAsync();
        var retried = await GetAsync(who, post);

        var target = retried.GetProperty("platforms")[0];
        Assert.Equal("scheduled", target.GetProperty("publishStatus").GetString());
        Assert.Equal(1, target.GetProperty("retryCount").GetInt32());
        Assert.True(DateTimeOffset.Parse(target.GetProperty("scheduledAt").GetString()!) > DateTimeOffset.UtcNow.AddSeconds(20));
        Assert.Equal("publishing", retried.GetProperty("status").GetString());
        Assert.Equal(HttpStatusCode.Conflict, (await PostAsync(who, post, "unschedule")).StatusCode);
    }

    // --- Who may do what -------------------------------------------------------------------------------------

    [Fact]
    public async Task AContentWriterInACompany_CreatesDraftsButCannotPublishArchiveOrRestore()
    {
        var owner = await OrganizationAsync();
        var writer = await AddMemberAsync(owner, OrganizationRole.ContentWriter);

        var draft = await CreateAsync(writer);

        Assert.Equal(HttpStatusCode.OK, (await Host.GetAsync("/v1/content/posts", writer.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(writer, draft, "schedule", new { scheduledAt = DateTimeOffset.UtcNow.AddHours(1) })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(writer, draft, "publish-now")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(writer, draft, "archive")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(writer, draft, "restore")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Host.DeleteAsync($"/v1/content/posts/{draft.GetProperty("id").GetGuid()}", writer.Token)).StatusCode);
    }

    private async Task<Member> AddMemberAsync(Member organization, OrganizationRole role)
    {
        var userId = Guid.NewGuid();
        await using var owner = await Host.Database.OpenOwnerAsync();
        await owner.ExecuteAsync("INSERT INTO users (id, email, password_hash, full_name) VALUES (@userId, @email, 'not-a-real-hash', 'Member')", new { userId, email = $"m-{userId:N}@example.com" });
        await owner.ExecuteAsync(
            "INSERT INTO agency_members (agency_id, user_id, business_model, role) SELECT @agencyId, @userId, business_model, @role FROM agencies WHERE id = @agencyId",
            new { agencyId = organization.Session.AgencyId, userId, role = role.ToString().ToLowerInvariant() });
        // The permissions a content writer of a company holds, read from the database like a sign-in does.
        var granted = (await owner.QueryAsync<string>(
            "SELECT permission_key FROM role_permissions WHERE business_model = 'company' AND role = @role AND is_granted", new { role = role.ToString().ToLowerInvariant() })).ToList();
        var token = Host.Factory.CreateAccessToken(userId, organization.Session.AgencyId, role, granted);
        return new Member(new Session("member", string.Empty, token, userId, organization.Session.AgencyId), organization.WorkspaceId);
    }

    [Fact]
    public async Task ACreator_SchedulesTheirOwnPost_BecauseTheirOnlyRoleHoldsPublish()
    {
        var creator = await OrganizationAsync("creator", "Instagram");
        var post = await CreateAsync(creator);

        var scheduled = await PostAsync(creator, post, "schedule", new { scheduledAt = DateTimeOffset.UtcNow.AddHours(1) });

        Assert.Equal(HttpStatusCode.OK, scheduled.StatusCode);
        Assert.Equal("scheduled", (await ApiTestHost.ReadJsonAsync(scheduled)).GetProperty("status").GetString());
    }

    // --- The rest ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task ADuplicate_IsANewDraftWithItsPlatformsPending_AndTagsHashtagsAndTemplatesWork()
    {
        var who = await OrganizationAsync();
        var post = await CreateAsync(who);

        var copy = await ApiTestHost.ReadJsonAsync(await PostAsync(who, post, "duplicate"));
        var tags = await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/content/tags?workspaceId={who.WorkspaceId}", who.Token));
        var suggestions = await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/content/hashtags/suggestions?workspaceId={who.WorkspaceId}&q=%23just", who.Token));
        var template = await Host.PostAsync("/v1/content/templates", new { workspaceId = who.WorkspaceId, name = "Dream Bigger", contentType = "social_post", title = "Dream Bigger", bodyDoc = Doc("Create."), hashtags = new[] { "#JustMakeIt" } }, who.Token);
        var again = await Host.PostAsync("/v1/content/templates", new { workspaceId = who.WorkspaceId, name = "dream bigger", contentType = "social_post", title = "x" }, who.Token);

        Assert.Equal("Copy of Dream Bigger", copy.GetProperty("title").GetString());
        Assert.Equal("draft", copy.GetProperty("status").GetString());
        Assert.Equal(1, tags.GetArrayLength());
        Assert.Equal("JustMakeIt", suggestions[0].GetProperty("hashtag").GetString());
        Assert.Equal(HttpStatusCode.Created, template.StatusCode);
        Assert.Equal("template_exists", Code(await ApiTestHost.ReadJsonAsync(again)));
        Assert.Equal(HttpStatusCode.NoContent, (await Host.DeleteAsync($"/v1/content/templates/{(await ApiTestHost.ReadJsonAsync(template)).GetProperty("id").GetGuid()}", who.Token)).StatusCode);
    }

    [Fact]
    public async Task Capabilities_SayWhatIsReal()
    {
        var who = await OrganizationAsync();

        var caps = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/content/capabilities", who.Token));

        Assert.True(caps.GetProperty("media").GetBoolean()); // the API host registers the Media Library
        Assert.False(caps.GetProperty("ai").GetBoolean());
        Assert.False(caps.GetProperty("campaigns").GetBoolean());
        Assert.Equal("fake", caps.GetProperty("publishing").GetString());
    }

    [Fact]
    public async Task TheDatabase_KeepsEveryRowInsideItsOrganization_WhatEverTheRepositoriesWrite()
    {
        var a = await OrganizationAsync();
        var b = await OrganizationAsync();
        await CreateAsync(a);
        await CreateAsync(b);

        await using var owner = await Host.Database.OpenOwnerAsync();
        foreach (var table in new[] { "content_posts", "content_hashtags", "content_post_tags", "content_post_platforms", "content_polls", "content_poll_options", "content_ctas", "content_locations", "content_revisions", "content_tags" })
        {
            // Every child row carries the organization of its post: the composite foreign keys leave no other way.
            var stray = await owner.QuerySingleAsync<long>(
                table == "content_posts" || table == "content_tags"
                    ? $"SELECT count(*) FROM {table} t JOIN workspaces w ON w.id = t.workspace_id WHERE w.agency_id <> t.agency_id"
                    : table == "content_poll_options"
                        ? "SELECT count(*) FROM content_poll_options o JOIN content_polls p ON p.id = o.poll_id WHERE p.agency_id <> o.agency_id"
                        : $"SELECT count(*) FROM {table} t JOIN content_posts p ON p.id = t.post_id WHERE p.agency_id <> t.agency_id");
            Assert.Equal(0, stray);
        }
    }

    // --- Files from the Media Library ------------------------------------------------------------------------

    private async Task<Guid> AssetAsync(Member who, string name = "photo.png", string mediaType = "image", Guid? workspaceId = null)
    {
        var id = Guid.NewGuid();
        await using var owner = await Host.Database.OpenOwnerAsync();
        await owner.ExecuteAsync(
            """
            INSERT INTO media_assets (id, agency_id, workspace_id, file_name, mime_type, media_type, file_size_bytes, storage_key)
            VALUES (@id, @agencyId, @workspaceId, @name, @mime, @mediaType, 1000, @key)
            """,
            new
            {
                id, agencyId = who.Session.AgencyId, workspaceId = workspaceId ?? who.WorkspaceId, name, mediaType,
                mime = mediaType == "image" ? "image/png" : mediaType == "video" ? "video/mp4" : "application/pdf",
                key = $"{who.Session.AgencyId:N}/{id:N}.bin"
            });
        return id;
    }

    private async Task<HttpResponseMessage> SaveWithFilesAsync(Member who, string type, params Guid[] assets) =>
        await Host.PostAsync("/v1/content/posts", new
        {
            workspaceId = who.WorkspaceId,
            contentType = type,
            title = "With files",
            bodyDoc = Doc("Look at these"),
            media = assets.Select(id => new { assetId = id }).ToArray()
        }, who.Token);

    private async Task<int> UsageCountAsync(Member who, Guid asset) =>
        (await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/media/assets/{asset}", who.Token))).GetProperty("usageCount").GetInt32();

    [Fact]
    public async Task APostKeepsItsFilesInOrder_WithRolesFromTheKindOfContent_AndTheLibrarySeesWhereTheyAreUsed()
    {
        var who = await OrganizationAsync();
        var first = await AssetAsync(who, "first.png");
        var second = await AssetAsync(who, "second.png");

        var created = await ApiTestHost.ReadJsonAsync(await SaveWithFilesAsync(who, "blog_post", first, second));
        var read = await GetAsync(who, created);

        var media = read.GetProperty("media").EnumerateArray().ToList();
        Assert.Equal([first, second], media.Select(m => m.GetProperty("assetId").GetGuid()));
        Assert.Equal(["cover", "attachment"], media.Select(m => m.GetProperty("role").GetString()));
        Assert.Equal(["first.png", "second.png"], media.Select(m => m.GetProperty("fileName").GetString()));
        Assert.Equal(1, await UsageCountAsync(who, first));
        Assert.Equal(1, await UsageCountAsync(who, second));

        // A file that a post uses is not deleted from the library.
        var refused = await Host.DeleteAsync($"/v1/media/assets/{first}", who.Token);
        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("asset_in_use", Code(await ApiTestHost.ReadJsonAsync(refused)));
    }

    [Fact]
    public async Task ACarouselsFilesAreItsSlides()
    {
        var who = await OrganizationAsync();
        var a = await AssetAsync(who);
        var b = await AssetAsync(who, "clip.mp4", "video");

        var created = await ApiTestHost.ReadJsonAsync(await SaveWithFilesAsync(who, "carousel", a, b));

        Assert.All(created.GetProperty("media").EnumerateArray(), m => Assert.Equal("slide", m.GetProperty("role").GetString()));
    }

    [Fact]
    public async Task TakingTheFilesOffThePost_FreesThemInTheLibrary()
    {
        var who = await OrganizationAsync();
        var asset = await AssetAsync(who);
        var created = await ApiTestHost.ReadJsonAsync(await SaveWithFilesAsync(who, "social_post", asset));

        var edit = await Host.PutAsync(
            $"/v1/content/posts/{created.GetProperty("id").GetGuid()}",
            new { expectedUpdatedAt = created.GetProperty("updatedAt"), title = "With files", bodyDoc = Doc("No files now") }, who.Token);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Empty((await ApiTestHost.ReadJsonAsync(edit)).GetProperty("media").EnumerateArray());
        Assert.Equal(0, await UsageCountAsync(who, asset));
        Assert.Equal(HttpStatusCode.NoContent, (await Host.DeleteAsync($"/v1/media/assets/{asset}", who.Token)).StatusCode);
    }

    [Fact]
    public async Task DeletingThePost_FreesItsFiles()
    {
        var who = await OrganizationAsync();
        var asset = await AssetAsync(who);
        var created = await ApiTestHost.ReadJsonAsync(await SaveWithFilesAsync(who, "social_post", asset));

        var deleted = await Host.DeleteAsync($"/v1/content/posts/{created.GetProperty("id").GetGuid()}", who.Token);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Equal(0, await UsageCountAsync(who, asset));
    }

    [Fact]
    public async Task ADuplicateCarriesTheFiles_AndTheLibraryCountsBothPosts()
    {
        var who = await OrganizationAsync();
        var asset = await AssetAsync(who);
        var created = await ApiTestHost.ReadJsonAsync(await SaveWithFilesAsync(who, "social_post", asset));

        var copy = await ApiTestHost.ReadJsonAsync(await PostAsync(who, created, "duplicate"));

        Assert.Equal(asset, copy.GetProperty("media")[0].GetProperty("assetId").GetGuid());
        Assert.Equal(2, await UsageCountAsync(who, asset));
    }

    [Fact]
    public async Task AFileOfAnotherOrganization_IsRefused_AsNotInTheLibrary()
    {
        var mine = await OrganizationAsync();
        var theirs = await OrganizationAsync();
        var foreign = await AssetAsync(theirs);

        var response = await SaveWithFilesAsync(mine, "social_post", foreign);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("media_asset_not_found", Code(await ApiTestHost.ReadJsonAsync(response)));
    }

    [Fact]
    public async Task AFileOfAnotherWorkspace_IsRefused()
    {
        var who = await OrganizationAsync();
        var other = await ApiTestHost.ReadJsonAsync(await Host.PostAsync("/v1/workspaces", new { name = "Second product" }, who.Token));
        var elsewhere = await AssetAsync(who, workspaceId: other.GetProperty("id").GetGuid());

        var response = await SaveWithFilesAsync(who, "social_post", elsewhere);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("media_workspace_mismatch", Code(await ApiTestHost.ReadJsonAsync(response)));
    }

    [Fact]
    public async Task ADocument_CannotBeOnAPost()
    {
        var who = await OrganizationAsync();
        var pdf = await AssetAsync(who, "brief.pdf", "document");

        var response = await SaveWithFilesAsync(who, "social_post", pdf);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("media_type_not_allowed", Code(await ApiTestHost.ReadJsonAsync(response)));
    }

    [Fact]
    public async Task ElevenFiles_AreOneTooMany()
    {
        var who = await OrganizationAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 11; i++)
        {
            ids.Add(await AssetAsync(who, $"f{i}.png"));
        }

        var response = await SaveWithFilesAsync(who, "carousel", [.. ids]);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").TryGetProperty("media", out _));
    }
}
