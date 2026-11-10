using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Dapper;
using Microsoft.Extensions.DependencyInjection;
using Platform.Helpers.Constants;
using Platform.Helpers.Media;
using Platform.Infrastructure.Data;
using Platform.IntegrationTests.Infrastructure;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// The Media Library end to end over a real database, connecting as <c>app_user</c> as in production, with the files in a folder of their
/// own: uploading and getting a file back byte for byte, the list and its filters and counts, folders, moving and copying, deleting (and
/// what a file in use is), who may do what, and that no organization sees another's files. The limit on a file is 1 MB here.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Database")]
public sealed class MediaApiTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ApiTestHost Host { get; set; } = null!;
    private string _storage = null!;

    public async Task InitializeAsync()
    {
        DapperConfiguration.Configure();
        _storage = Path.Combine(Path.GetTempPath(), "jmi-media-api-" + Guid.NewGuid().ToString("N"));
        Host = await ApiTestHost.StartWithDatabaseAsync(postgres, configure: (settings, _) =>
        {
            settings["Media:Storage:RootPath"] = _storage;
            settings["Media:MaxFileMegabytes"] = "1";
        });
    }

    public async Task DisposeAsync()
    {
        await Host.DisposeAsync();
        if (Directory.Exists(_storage))
        {
            Directory.Delete(_storage, recursive: true);
        }
    }

    private static readonly CancellationToken Ct = CancellationToken.None;

    private sealed record Member(Session Session, Guid WorkspaceId)
    {
        public string Token => Session.AccessToken;
    }

    private async Task<Member> OrganizationAsync(string model = "company")
    {
        var session = await Host.RegisterAsync(businessModel: model);
        var workspaces = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/workspaces", session.AccessToken));
        return new Member(session, workspaces[0].GetProperty("id").GetGuid());
    }

    // A member of the organization with the permissions their role holds in a company, read from the database like a sign-in does.
    private async Task<Member> AddMemberAsync(Member organization, OrganizationRole role)
    {
        var userId = Guid.NewGuid();
        await using var owner = await Host.Database.OpenOwnerAsync();
        await owner.ExecuteAsync("INSERT INTO users (id, email, password_hash, full_name) VALUES (@userId, @email, 'not-a-real-hash', 'Member')", new { userId, email = $"m-{userId:N}@example.com" });
        await owner.ExecuteAsync(
            "INSERT INTO agency_members (agency_id, user_id, business_model, role) SELECT @agencyId, @userId, business_model, @role FROM agencies WHERE id = @agencyId",
            new { agencyId = organization.Session.AgencyId, userId, role = role.ToString().ToLowerInvariant() });
        var granted = (await owner.QueryAsync<string>(
            "SELECT permission_key FROM role_permissions WHERE business_model = 'company' AND role = @role AND is_granted", new { role = role.ToString().ToLowerInvariant() })).ToList();
        var token = Host.Factory.CreateAccessToken(userId, organization.Session.AgencyId, role, granted);
        return new Member(new Session("member", string.Empty, token, userId, organization.Session.AgencyId), organization.WorkspaceId);
    }

    private static byte[] Png(int width = 640, int height = 480, int length = 300)
    {
        var bytes = new byte[length];
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13, (byte)'I', (byte)'H', (byte)'D', (byte)'R'];
        header.CopyTo(bytes, 0);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(16), width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes.AsSpan(20), height);
        for (var i = 24; i < length; i++)
        {
            bytes[i] = (byte)(i * 7);
        }

        return bytes;
    }

    private async Task<HttpResponseMessage> UploadAsync(
        Member who, byte[]? bytes = null, string fileName = "photo.png", Guid? folderId = null, string[]? tags = null, Guid? workspaceId = null, string declaredType = "image/png")
    {
        var form = new MultipartFormDataContent { { new StringContent((workspaceId ?? who.WorkspaceId).ToString()), "workspaceId" } };
        if (folderId is not null)
        {
            form.Add(new StringContent(folderId.Value.ToString()), "folderId");
        }

        foreach (var tag in tags ?? [])
        {
            form.Add(new StringContent(tag), "tags");
        }

        var file = new ByteArrayContent(bytes ?? Png());
        file.Headers.ContentType = new MediaTypeHeaderValue(declaredType);
        form.Add(file, "file", fileName);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/media/assets") { Content = form };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", who.Token);
        return await Host.Client.SendAsync(request);
    }

    private async Task<JsonElement> UploadedAsync(Member who, byte[]? bytes = null, string fileName = "photo.png", Guid? folderId = null, string[]? tags = null)
    {
        var response = await UploadAsync(who, bytes, fileName, folderId, tags);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private static Guid IdOf(JsonElement element) => element.GetProperty("id").GetGuid();

    private async Task<JsonElement> ListAsync(Member who, string query = "") =>
        await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/media/assets?workspaceId={who.WorkspaceId}{query}", who.Token));

    private async Task<JsonElement> FolderAsync(Member who, string name)
    {
        var response = await Host.PostAsync("/v1/media/folders", new { workspaceId = who.WorkspaceId, name }, who.Token);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await ApiTestHost.ReadJsonAsync(response);
    }

    private static string Code(JsonElement problem) => problem.GetProperty("code").GetString()!;

    private string StoredPath(JsonElement asset)
    {
        using var owner = Host.Database.OpenOwnerAsync().GetAwaiter().GetResult();
        var key = owner.QuerySingle<string>("SELECT storage_key FROM media_assets WHERE id = @id", new { id = IdOf(asset) });
        return Path.Combine(_storage, key);
    }

    // --- Uploading and getting the file back -----------------------------------------------------------------

    [Fact]
    public async Task APng_IsStoredAndReadBackByteForByte_WithItsSizeReadFromTheFile()
    {
        var who = await OrganizationAsync();
        var bytes = Png(1920, 1080);

        var created = await UploadedAsync(who, bytes, "Summer Beach.png", tags: ["Summer", "#beach"]);

        Assert.Equal("Summer Beach.png", created.GetProperty("fileName").GetString());
        Assert.Equal("image/png", created.GetProperty("mimeType").GetString());
        Assert.Equal("image", created.GetProperty("mediaType").GetString());
        Assert.Equal(bytes.Length, created.GetProperty("sizeBytes").GetInt64());
        Assert.Equal(1920, created.GetProperty("width").GetInt32());
        Assert.Equal(1080, created.GetProperty("height").GetInt32());
        Assert.Equal(["beach", "Summer"], created.GetProperty("tags").EnumerateArray().Select(t => t.GetProperty("name").GetString()));
        Assert.Equal(bytes, await File.ReadAllBytesAsync(StoredPath(created), Ct));

        var file = await Host.GetAsync($"/v1/media/assets/{IdOf(created)}/file", who.Token);
        Assert.Equal(HttpStatusCode.OK, file.StatusCode);
        Assert.Equal("image/png", file.Content.Headers.ContentType?.MediaType);
        Assert.Equal("nosniff", file.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("sandbox", file.Headers.GetValues("Content-Security-Policy").Single());
        Assert.Equal(bytes, await file.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task AFile_CanBeReadInPieces_WhichAVideoNeedsToSeek()
    {
        var who = await OrganizationAsync();
        var created = await UploadedAsync(who, Png(length: 1000));

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/v1/media/assets/{IdOf(created)}/file");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", who.Token);
        request.Headers.Range = new RangeHeaderValue(10, 19);
        var response = await Host.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.PartialContent, response.StatusCode);
        Assert.Equal(Png(length: 1000)[10..20], await response.Content.ReadAsByteArrayAsync(Ct));
    }

    [Fact]
    public async Task ADocument_IsAlwaysADownload_AndAnyFileIsWhenAsked()
    {
        var who = await OrganizationAsync();
        byte[] pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[200]];
        var document = await UploadedAsync(who, pdf, "brief.pdf");
        var picture = await UploadedAsync(who);

        Assert.Equal("document", document.GetProperty("mediaType").GetString());
        var documentResponse = await Host.GetAsync($"/v1/media/assets/{IdOf(document)}/file", who.Token);
        Assert.Equal("attachment", documentResponse.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("brief.pdf", documentResponse.Content.Headers.ContentDisposition?.FileNameStar ?? documentResponse.Content.Headers.ContentDisposition?.FileName);

        Assert.Null((await Host.GetAsync($"/v1/media/assets/{IdOf(picture)}/file", who.Token)).Content.Headers.ContentDisposition);
        Assert.Equal("attachment", (await Host.GetAsync($"/v1/media/assets/{IdOf(picture)}/file?download=true", who.Token)).Content.Headers.ContentDisposition?.DispositionType);
    }

    [Theory]
    [InlineData("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>", "logo.png", "image/png")]
    [InlineData("MZ this is a program", "photo.png", "image/png")]
    [InlineData("<html><script>alert(1)</script></html>", "page.html", "text/html")]
    public async Task AFileOfATypeThatIsNotAllowed_IsRefusedWhateverItIsCalled_AndNothingIsKept(string content, string name, string declared)
    {
        var who = await OrganizationAsync();

        var response = await UploadAsync(who, System.Text.Encoding.ASCII.GetBytes(content), name, declaredType: declared);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ApiTestHost.ReadJsonAsync(response);
        Assert.Equal("validation_failed", Code(problem));
        Assert.Contains("file", problem.GetProperty("errors").EnumerateObject().Select(e => e.Name));
        Assert.Equal(0, (await ListAsync(who)).GetProperty("totalCount").GetInt32());
        Assert.False(Directory.Exists(_storage) && Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).Length > 0);
    }

    [Fact]
    public async Task ThePictureIsStoredAsWhatItIs_NotAsWhatItsNameOrTheBrowserSays()
    {
        var who = await OrganizationAsync();

        var created = await UploadedAsync(who, Png(), "report.pdf");

        Assert.Equal("image/png", created.GetProperty("mimeType").GetString());
        Assert.EndsWith(".png", StoredPath(created));
    }

    [Fact]
    public async Task AFileOverTheLimit_IsRefused_AndNothingIsKept()
    {
        var who = await OrganizationAsync();

        var response = await UploadAsync(who, Png(length: 1024 * 1024 + 1024));

        Assert.Contains(response.StatusCode, new[] { HttpStatusCode.BadRequest, HttpStatusCode.RequestEntityTooLarge });
        Assert.Equal(0, (await ListAsync(who)).GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task APathInTheFileName_IsCutToTheName_AndTheKeyIsMadeFromIds()
    {
        var who = await OrganizationAsync();

        var created = await UploadedAsync(who, fileName: "..\\..\\secret\\holiday.png");

        Assert.Equal("holiday.png", created.GetProperty("fileName").GetString());
        Assert.StartsWith(_storage, StoredPath(created));
        Assert.True(File.Exists(StoredPath(created)));
    }

    [Fact]
    public async Task AnUpload_NeedsAWorkspaceOfTheOrganization_AndAFolderOfThatWorkspace()
    {
        var mine = await OrganizationAsync();
        var theirs = await OrganizationAsync();
        var theirFolder = await FolderAsync(theirs, "Private");

        var inTheirWorkspace = await UploadAsync(mine, workspaceId: theirs.WorkspaceId);
        var inTheirFolder = await UploadAsync(mine, folderId: IdOf(theirFolder));
        var none = await Host.Client.SendAsync(new HttpRequestMessage(HttpMethod.Post, "/v1/media/assets")
        {
            Content = new MultipartFormDataContent { { new ByteArrayContent(Png()), "file", "a.png" } },
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", mine.Token) }
        });

        Assert.Equal(HttpStatusCode.NotFound, inTheirWorkspace.StatusCode);
        Assert.Equal("workspace_not_found", Code(await ApiTestHost.ReadJsonAsync(inTheirWorkspace)));
        Assert.Equal(HttpStatusCode.NotFound, inTheirFolder.StatusCode);
        Assert.Equal("folder_not_found", Code(await ApiTestHost.ReadJsonAsync(inTheirFolder)));
        Assert.Equal(HttpStatusCode.BadRequest, none.StatusCode);
        Assert.Equal(0, (await ListAsync(mine)).GetProperty("totalCount").GetInt32());
        Assert.False(Directory.Exists(_storage) && Directory.GetFiles(_storage, "*", SearchOption.AllDirectories).Length > 0);
    }

    // --- The list ---------------------------------------------------------------------------------------

    [Fact]
    public async Task TheList_HasTheCountOfEachTab_FiltersByTypeFolderTagWordsAndSize_AndPages()
    {
        var who = await OrganizationAsync();
        var folder = await FolderAsync(who, "Social Posts");
        byte[] mp4 = [0, 0, 0, 0x18, .. "ftypisom"u8.ToArray(), .. new byte[100]];
        byte[] pdf = [.. "%PDF-1.7\n"u8.ToArray(), .. new byte[100]];
        await UploadedAsync(who, Png(), "mountains.png", IdOf(folder), ["travel"]);
        await UploadedAsync(who, Png(), "city.png", tags: ["Travel", "urban"]);
        await UploadedAsync(who, mp4, "clip.mp4");
        await UploadedAsync(who, pdf, "brief.pdf");

        var all = await ListAsync(who);
        Assert.Equal(4, all.GetProperty("totalCount").GetInt32());
        var counts = all.GetProperty("typeCounts");
        Assert.Equal((4, 2, 1, 1, 0), (counts.GetProperty("all").GetInt32(), counts.GetProperty("image").GetInt32(), counts.GetProperty("video").GetInt32(), counts.GetProperty("document").GetInt32(), counts.GetProperty("other").GetInt32()));

        var videos = await ListAsync(who, "&type=video");
        Assert.Equal(["clip.mp4"], videos.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()));
        Assert.Equal(4, videos.GetProperty("typeCounts").GetProperty("all").GetInt32());   // the tabs keep their numbers
        Assert.Equal(["mountains.png"], (await ListAsync(who, $"&folderId={IdOf(folder)}")).GetProperty("items").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()));
        Assert.Equal(3, (await ListAsync(who, "&uncategorized=true")).GetProperty("totalCount").GetInt32());
        Assert.Equal(["city.png", "mountains.png"], (await ListAsync(who, "&q=trav&sort=name")).GetProperty("items").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()));
        Assert.Equal(["brief.pdf"], (await ListAsync(who, "&q=BRIEF")).GetProperty("items").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()));
        Assert.Equal(4, (await ListAsync(who, "&size=small")).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await ListAsync(who, "&size=large")).GetProperty("totalCount").GetInt32());

        var firstPage = await ListAsync(who, "&pageSize=3&sort=name");
        Assert.Equal((3, 2, 4), (firstPage.GetProperty("items").GetArrayLength(), firstPage.GetProperty("totalPages").GetInt32(), firstPage.GetProperty("totalCount").GetInt32()));
        Assert.Single((await ListAsync(who, "&pageSize=3&sort=name&page=2")).GetProperty("items").EnumerateArray());
    }

    [Fact]
    public async Task ASearchWithAPercentSign_MeansThatCharacter_NotEverything()
    {
        var who = await OrganizationAsync();
        await UploadedAsync(who, fileName: "100% sure.png");
        await UploadedAsync(who, fileName: "other.png");

        var found = await ListAsync(who, "&q=%25");

        Assert.Equal(["100% sure.png"], found.GetProperty("items").EnumerateArray().Select(a => a.GetProperty("fileName").GetString()));
    }

    [Fact]
    public async Task ABadQuery_IsRefusedFieldByField()
    {
        var who = await OrganizationAsync();

        var response = await Host.GetAsync($"/v1/media/assets?workspaceId={who.WorkspaceId}&type=audio&sort=name%3B%20drop&pageSize=500", who.Token);
        var missing = await Host.GetAsync("/v1/media/assets", who.Token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = (await ApiTestHost.ReadJsonAsync(response)).GetProperty("errors").EnumerateObject().Select(e => e.Name).Order().ToList();
        Assert.Equal(["pageSize", "sort", "type"], errors);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode);
    }

    [Fact]
    public async Task TheConfig_SaysTheLimit_AndTheExtensions()
    {
        var who = await OrganizationAsync();

        var config = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/media/config", who.Token));

        Assert.Equal(1024 * 1024, config.GetProperty("maxFileBytes").GetInt64());
        Assert.Contains(".png", config.GetProperty("extensions").EnumerateArray().Select(e => e.GetString()));
    }

    // --- Folders, moving and copying ---------------------------------------------------------------------

    [Fact]
    public async Task TheOverview_CountsAllMediaUncategorizedAndEachFolderAndTag()
    {
        var who = await OrganizationAsync();
        var folder = await FolderAsync(who, "Videos");
        await UploadedAsync(who, folderId: IdOf(folder), tags: ["a"]);
        await UploadedAsync(who, tags: ["a", "b"]);

        var overview = await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/media/overview?workspaceId={who.WorkspaceId}", who.Token));

        Assert.Equal(2, overview.GetProperty("allCount").GetInt32());
        Assert.Equal(1, overview.GetProperty("uncategorizedCount").GetInt32());
        Assert.Equal(("Videos", 1), (overview.GetProperty("folders")[0].GetProperty("name").GetString(), overview.GetProperty("folders")[0].GetProperty("assetCount").GetInt32()));
        Assert.Equal([("a", 2), ("b", 1)], overview.GetProperty("tags").EnumerateArray().Select(t => (t.GetProperty("name").GetString(), t.GetProperty("assetCount").GetInt32())));
    }

    [Fact]
    public async Task AFolderNameThatExists_IsA409_InAnyLetterCase()
    {
        var who = await OrganizationAsync();
        await FolderAsync(who, "Videos");

        var again = await Host.PostAsync("/v1/media/folders", new { workspaceId = who.WorkspaceId, name = "videos" }, who.Token);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("folder_exists", Code(await ApiTestHost.ReadJsonAsync(again)));
    }

    [Fact]
    public async Task AMoveReplacesTheFolders_ACopyAddsOne_AndAMoveToNoneMakesTheFileUncategorized()
    {
        var who = await OrganizationAsync();
        var one = await FolderAsync(who, "One");
        var two = await FolderAsync(who, "Two");
        var file = await UploadedAsync(who, folderId: IdOf(one));
        var id = IdOf(file);

        Assert.Equal(HttpStatusCode.NoContent, (await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { id }, folderId = IdOf(two), mode = "copy" }, who.Token)).StatusCode);
        Assert.Equal(2, (await Folders(who, id)).Count);

        Assert.Equal(HttpStatusCode.NoContent, (await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { id }, folderId = IdOf(one), mode = "move" }, who.Token)).StatusCode);
        Assert.Equal([IdOf(one)], await Folders(who, id));

        Assert.Equal(HttpStatusCode.NoContent, (await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { id }, folderId = (Guid?)null, mode = "move" }, who.Token)).StatusCode);
        Assert.Empty(await Folders(who, id));
    }

    private async Task<List<Guid>> Folders(Member who, Guid asset) =>
        (await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/media/assets/{asset}", who.Token))).GetProperty("folderIds").EnumerateArray().Select(f => f.GetGuid()).ToList();

    [Fact]
    public async Task FilesAndFolders_OfDifferentWorkspaces_CannotBeMixed_AndNothingIsChangedWhenOneIdIsWrong()
    {
        var who = await OrganizationAsync();
        var folder = await FolderAsync(who, "One");
        var mine = await UploadedAsync(who);
        Guid secondWorkspace;
        await using (var owner = await Host.Database.OpenOwnerAsync())
        {
            secondWorkspace = await owner.ExecuteScalarAsync<Guid>("INSERT INTO workspaces (agency_id, name) VALUES (@a, 'Second') RETURNING id", new { a = who.Session.AgencyId });
        }

        var other = await UploadedAsync(new Member(who.Session, secondWorkspace));

        var mixed = await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { IdOf(mine), IdOf(other) }, folderId = IdOf(folder), mode = "copy" }, who.Token);
        var oneMissing = await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { IdOf(mine), Guid.NewGuid() }, folderId = IdOf(folder), mode = "copy" }, who.Token);

        Assert.Equal(HttpStatusCode.Conflict, mixed.StatusCode);
        Assert.Equal("workspace_mismatch", Code(await ApiTestHost.ReadJsonAsync(mixed)));
        Assert.Equal(HttpStatusCode.NotFound, oneMissing.StatusCode);
        Assert.Empty(await Folders(who, IdOf(mine)));
    }

    [Fact]
    public async Task ADeletedFolder_LeavesItsFilesInTheLibrary()
    {
        var who = await OrganizationAsync();
        var folder = await FolderAsync(who, "Temporary");
        var file = await UploadedAsync(who, folderId: IdOf(folder));

        Assert.Equal(HttpStatusCode.NoContent, (await Host.DeleteAsync($"/v1/media/folders/{IdOf(folder)}", who.Token)).StatusCode);

        Assert.Empty(await Folders(who, IdOf(file)));
        Assert.Equal(1, (await ListAsync(who)).GetProperty("totalCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await Host.DeleteAsync($"/v1/media/folders/{IdOf(folder)}", who.Token)).StatusCode);
    }

    // --- Deleting ---------------------------------------------------------------------------------------

    [Fact]
    public async Task ADeletedFile_IsGone_AndSoAreItsBytes()
    {
        var who = await OrganizationAsync();
        var created = await UploadedAsync(who);
        var path = StoredPath(created);
        Assert.True(File.Exists(path));

        var response = await Host.DeleteAsync($"/v1/media/assets/{IdOf(created)}", who.Token);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(File.Exists(path));
        Assert.Equal(HttpStatusCode.NotFound, (await Host.GetAsync($"/v1/media/assets/{IdOf(created)}", who.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.GetAsync($"/v1/media/assets/{IdOf(created)}/file", who.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.DeleteAsync($"/v1/media/assets/{IdOf(created)}", who.Token)).StatusCode);
    }

    [Fact]
    public async Task AFileThatContentUses_IsA409_AndItsBytesStay_UntilTheUsageIsGone()
    {
        var who = await OrganizationAsync();
        var created = await UploadedAsync(who);
        var path = StoredPath(created);
        await using (var owner = await Host.Database.OpenOwnerAsync())
        {
            await owner.ExecuteAsync(
                "INSERT INTO media_asset_usages (agency_id, workspace_id, media_asset_id, module_name, entity_type, entity_id) VALUES (@a, @w, @asset, 'content_studio', 'post', gen_random_uuid())",
                new { a = who.Session.AgencyId, w = who.WorkspaceId, asset = IdOf(created) });
        }

        var refused = await Host.DeleteAsync($"/v1/media/assets/{IdOf(created)}", who.Token);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal("asset_in_use", Code(await ApiTestHost.ReadJsonAsync(refused)));
        Assert.True(File.Exists(path));
        Assert.Equal(1, (await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/media/assets/{IdOf(created)}", who.Token))).GetProperty("usageCount").GetInt32());

        await using (var owner = await Host.Database.OpenOwnerAsync())
        {
            await owner.ExecuteAsync("DELETE FROM media_asset_usages WHERE media_asset_id = @asset", new { asset = IdOf(created) });
        }

        Assert.Equal(HttpStatusCode.NoContent, (await Host.DeleteAsync($"/v1/media/assets/{IdOf(created)}", who.Token)).StatusCode);
        Assert.False(File.Exists(path));
    }

    // --- Who may do what ---------------------------------------------------------------------------------

    [Fact]
    public async Task AClientManager_ReadsButCannotAddOrganizeOrDelete()
    {
        var owner = await OrganizationAsync();
        var created = await UploadedAsync(owner);
        var reader = await AddMemberAsync(owner, OrganizationRole.ClientManager);

        Assert.Equal(HttpStatusCode.OK, (await Host.GetAsync($"/v1/media/assets?workspaceId={owner.WorkspaceId}", reader.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Host.GetAsync($"/v1/media/assets/{IdOf(created)}/file", reader.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await Host.GetAsync($"/v1/media/overview?workspaceId={owner.WorkspaceId}", reader.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await UploadAsync(reader)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Host.PostAsync("/v1/media/folders", new { workspaceId = owner.WorkspaceId, name = "X" }, reader.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { IdOf(created) }, folderId = (Guid?)null, mode = "move" }, reader.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Host.DeleteAsync($"/v1/media/assets/{IdOf(created)}", reader.Token)).StatusCode);
    }

    [Fact]
    public async Task AContentWriterInACompany_AddsAndOrganizesButCannotDelete()
    {
        var owner = await OrganizationAsync();
        var writer = await AddMemberAsync(owner, OrganizationRole.ContentWriter);

        var created = await UploadedAsync(writer);
        var folder = await FolderAsync(writer, "Mine");

        Assert.Equal(HttpStatusCode.NoContent, (await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { IdOf(created) }, folderId = IdOf(folder), mode = "copy" }, writer.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Host.DeleteAsync($"/v1/media/assets/{IdOf(created)}", writer.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await Host.DeleteAsync($"/v1/media/folders/{IdOf(folder)}", writer.Token)).StatusCode);
    }

    [Fact]
    public async Task ACreator_DeletesTheirOwnFiles_BecauseTheirOnlyRoleHoldsDelete()
    {
        var creator = await OrganizationAsync("creator");
        var created = await UploadedAsync(creator);

        Assert.Equal(HttpStatusCode.NoContent, (await Host.DeleteAsync($"/v1/media/assets/{IdOf(created)}", creator.Token)).StatusCode);
    }

    [Fact]
    public async Task AnonymousRequests_AreRefused_ForTheListAndForTheBytes()
    {
        var who = await OrganizationAsync();
        var created = await UploadedAsync(who);

        Assert.Equal(HttpStatusCode.Unauthorized, (await Host.GetAsync($"/v1/media/assets?workspaceId={who.WorkspaceId}")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Host.GetAsync($"/v1/media/assets/{IdOf(created)}/file")).StatusCode);
    }

    // --- Isolation -------------------------------------------------------------------------------------

    [Fact]
    public async Task NoOrganization_SeesChangesOrDownloads_AnotherOrganizationsFiles()
    {
        var mine = await OrganizationAsync();
        var theirs = await OrganizationAsync();
        var theirFile = await UploadedAsync(theirs, tags: ["secret"]);
        var theirFolder = await FolderAsync(theirs, "Private");
        var myFile = await UploadedAsync(mine);
        var myFolder = await FolderAsync(mine, "Mine");

        Assert.Equal(0, (await ListAsync(mine)).GetProperty("typeCounts").GetProperty("all").GetInt32() - 1);   // only my own one file
        Assert.Equal(0, (await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/media/assets?workspaceId={theirs.WorkspaceId}", mine.Token))).GetProperty("totalCount").GetInt32());
        Assert.Equal(0, (await ApiTestHost.ReadJsonAsync(await Host.GetAsync($"/v1/media/overview?workspaceId={theirs.WorkspaceId}", mine.Token))).GetProperty("allCount").GetInt32());
        Assert.Equal(HttpStatusCode.NotFound, (await Host.GetAsync($"/v1/media/assets/{IdOf(theirFile)}", mine.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.GetAsync($"/v1/media/assets/{IdOf(theirFile)}/file", mine.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.DeleteAsync($"/v1/media/assets/{IdOf(theirFile)}", mine.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.DeleteAsync($"/v1/media/folders/{IdOf(theirFolder)}", mine.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { IdOf(theirFile) }, folderId = IdOf(myFolder), mode = "copy" }, mine.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await Host.PostAsync("/v1/media/assets/organize", new { assetIds = new[] { IdOf(myFile) }, folderId = IdOf(theirFolder), mode = "copy" }, mine.Token)).StatusCode);
        Assert.True(File.Exists(StoredPath(theirFile)));
    }

    // --- The contract other features use --------------------------------------------------------------------

    [Fact]
    public async Task TheCatalog_FindsAnOrganizationsOwnFiles_AndNotAnotherOnes()
    {
        var mine = await OrganizationAsync();
        var theirs = await OrganizationAsync();
        var myFile = await UploadedAsync(mine, Png(800, 600));
        var theirFile = await UploadedAsync(theirs);

        await using var scope = Host.Factory.Services.CreateAsyncScope();
        var catalog = scope.ServiceProvider.GetRequiredService<IMediaAssetCatalog>();
        var found = await catalog.FindAsync(mine.Session.AgencyId, mine.Session.UserId, [IdOf(myFile), IdOf(theirFile), Guid.NewGuid()], Ct);

        var summary = Assert.Single(found);
        Assert.Equal(IdOf(myFile), summary.Id);
        Assert.Equal(("image", "image/png", 800, 600), (summary.MediaType, summary.MimeType, summary.Width, summary.Height));
        Assert.Equal(mine.WorkspaceId, summary.WorkspaceId);
        Assert.Empty(await catalog.FindAsync(mine.Session.AgencyId, mine.Session.UserId, [], Ct));
    }

    [Fact]
    public async Task ContentStudio_SeesTheMediaLibrary_ThroughTheContract()
    {
        var who = await OrganizationAsync();

        var capabilities = await ApiTestHost.ReadJsonAsync(await Host.GetAsync("/v1/content/capabilities", who.Token));

        Assert.True(capabilities.GetProperty("media").GetBoolean());
    }
}
