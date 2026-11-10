using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.Content.DTOs;
using Platform.Content.Entities;
using Platform.Content.Features.Hashtags;
using Platform.Content.Features.Templates;
using Platform.Content.Interfaces;
using Platform.Content.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;

namespace Platform.Content.Tests;

public sealed class HashtagServiceTests : ServiceTestBase
{
    private readonly IHashtagRepository _hashtags = Substitute.For<IHashtagRepository>();
    private readonly HashtagService _service;
    private static readonly CancellationToken Ct = CancellationToken.None;

    public HashtagServiceTests() =>
        _service = new HashtagService(_hashtags, Lookups, Tenant, NullLogger<HashtagService>.Instance);

    [Fact]
    public async Task Every_Method_NeedsItsPermission()
    {
        SignIn();

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.SuggestAsync(new HashtagSuggestionQuery(), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ListTagsAsync(null, Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateTagAsync(new CreateTagRequest(TestData.WorkspaceId, "x", null), Ct));
        Assert.Empty(_hashtags.ReceivedCalls());

        SignIn(PermissionKeys.ContentStudioView);
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateTagAsync(new CreateTagRequest(TestData.WorkspaceId, "x", null), Ct));
    }

    [Theory]
    [InlineData("#gro", "gro")]
    [InlineData("  ##gro ", "gro")]
    [InlineData("gro", "gro")]
    public async Task Suggest_SearchesForWhatWasTypedAfterTheHash(string typed, string prefix)
    {
        SignIn(PermissionKeys.ContentStudioView);
        _hashtags.SuggestAsync(default, default, default, default, default, default).ReturnsForAnyArgs([("GrowTogether", 4), ("Growth", 2)]);

        var result = await _service.SuggestAsync(new HashtagSuggestionQuery(TestData.WorkspaceId, typed, 5), Ct);

        Assert.Equal([("GrowTogether", 4), ("Growth", 2)], result.Select(r => (r.Hashtag, r.Uses)));
        await _hashtags.Received(1).SuggestAsync(TestData.AgencyId, TestData.UserId, TestData.WorkspaceId, prefix, 5, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  # ")]
    public async Task Suggest_WithNothingTyped_AsksForTheMostUsed(string? typed)
    {
        SignIn(PermissionKeys.ContentStudioView);
        _hashtags.SuggestAsync(default, default, default, default, default, default).ReturnsForAnyArgs([]);

        await _service.SuggestAsync(new HashtagSuggestionQuery(Q: typed), Ct);

        await _hashtags.Received(1).SuggestAsync(TestData.AgencyId, TestData.UserId, null, null, HashtagSuggestionQuery.DefaultLimit, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(31)]
    public async Task Suggest_RefusesALimitOutsideOneToThirty(int limit)
    {
        SignIn(PermissionKeys.ContentStudioView);

        await Assert.ThrowsAsync<BadRequestException>(() => _service.SuggestAsync(new HashtagSuggestionQuery(Limit: limit), Ct));
    }

    [Fact]
    public async Task ListTags_ReturnsTheTagsWithTheirColours()
    {
        SignIn(PermissionKeys.ContentStudioView);
        var id = Guid.NewGuid();
        _hashtags.ListTagsAsync(TestData.AgencyId, TestData.UserId, TestData.WorkspaceId, Arg.Any<CancellationToken>())
            .Returns([new ContentTag { Id = id, Name = "Motivation", Color = "#1F7A55" }]);

        var tags = await _service.ListTagsAsync(TestData.WorkspaceId, Ct);

        Assert.Equal([new TagResponse(id, "Motivation", "#1F7A55")], tags);
    }

    [Fact]
    public async Task CreateTag_StoresTheTrimmedNameWithAColourPickedFromTheName()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        _hashtags.InsertTagAsync(default, default!, default).ReturnsForAnyArgs(true);

        var tag = await _service.CreateTagAsync(new CreateTagRequest(TestData.WorkspaceId, "  Motivation ", null), Ct);

        Assert.Equal(("Motivation", TagColors.For("Motivation")), (tag.Name, tag.Color));
        await _hashtags.Received(1).InsertTagAsync(
            TestData.UserId,
            Arg.Is<ContentTag>(t => t.AgencyId == TestData.AgencyId && t.WorkspaceId == TestData.WorkspaceId && t.Name == "Motivation"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CreateTag_KeepsAColourThatWasGiven()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        _hashtags.InsertTagAsync(default, default!, default).ReturnsForAnyArgs(true);

        var tag = await _service.CreateTagAsync(new CreateTagRequest(TestData.WorkspaceId, "Brand", "#2563EB"), Ct);

        Assert.Equal("#2563EB", tag.Color);
    }

    [Theory]
    [InlineData("", "#1F7A55")]
    [InlineData("a\nb", "#1F7A55")]
    [InlineData("ok", "green")]
    public async Task CreateTag_ChecksTheFields(string name, string colour)
    {
        SignIn(PermissionKeys.ContentStudioCreate);

        await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateTagAsync(new CreateTagRequest(TestData.WorkspaceId, name, colour), Ct));
        await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateTagAsync(new CreateTagRequest(null, "ok", null), Ct));
        await _hashtags.DidNotReceiveWithAnyArgs().InsertTagAsync(default, default!, default);
    }

    [Fact]
    public async Task CreateTag_InAWorkspaceThatIsNotTheirs_IsNotFound_AndAnExistingNameIsAConflict()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(false);
        var missing = await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateTagAsync(new CreateTagRequest(TestData.WorkspaceId, "x", null), Ct));
        Assert.Equal("workspace_not_found", missing.Code);

        Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(true);
        _hashtags.InsertTagAsync(default, default!, default).ReturnsForAnyArgs(false);
        var exists = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateTagAsync(new CreateTagRequest(TestData.WorkspaceId, "x", null), Ct));
        Assert.Equal("tag_exists", exists.Code);
    }
}

public sealed class TemplateServiceTests : ServiceTestBase
{
    private readonly ITemplateRepository _templates = Substitute.For<ITemplateRepository>();
    private readonly TemplateService _service;
    private static readonly CancellationToken Ct = CancellationToken.None;

    public TemplateServiceTests()
    {
        _service = new TemplateService(_templates, Lookups, Tenant, Time, NullLogger<TemplateService>.Instance);
        _templates.InsertAsync(default!, default).ReturnsForAnyArgs(true);
    }

    private static SaveTemplateRequest Valid(string name = "Dream Bigger") => new()
    {
        WorkspaceId = TestData.WorkspaceId, Name = name, Description = "A motivational opener", ContentType = "social_post",
        Title = "Dream Bigger", BodyDoc = TestData.ParagraphDoc("Create. Inspire."), Hashtags = ["#JustMakeIt", "creativity"]
    };

    [Fact]
    public async Task Every_Method_NeedsItsPermission()
    {
        SignIn();
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.ListAsync(new TemplateListQuery(), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateAsync(Valid(), Ct));
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.DeleteAsync(Guid.NewGuid(), Ct));

        SignIn(PermissionKeys.ContentStudioView);
        await Assert.ThrowsAsync<ForbiddenException>(() => _service.CreateAsync(Valid(), Ct));
        Assert.Empty(_templates.ReceivedCalls());
    }

    [Fact]
    public async Task Create_KeepsTheTitleThePlainTextTheDocumentAndTheNormalizedHashtags()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        ContentTemplate? stored = null;
        _templates.InsertAsync(Arg.Do<ContentTemplate>(t => stored = t), Arg.Any<CancellationToken>()).Returns(true);

        var response = await _service.CreateAsync(Valid(), Ct);

        Assert.Equal(("Dream Bigger", "Dream Bigger", "Create. Inspire."), (stored!.Name, stored.TitleTemplate, stored.BodyTemplate));
        Assert.Equal((TestData.AgencyId, TestData.UserId, true), (stored.AgencyId, stored.CreatedBy, stored.IsActive));
        Assert.Equal(["JustMakeIt", "creativity"], response.Hashtags);
        Assert.Equal(System.Text.Json.JsonValueKind.Object, response.BodyDoc!.Value.ValueKind);
        Assert.Equal("Create. Inspire.", response.BodyText);

        // What was stored comes back out the same way.
        var listed = await ListedAs(stored);
        Assert.Equal(response.Hashtags, listed.Hashtags);
        Assert.Equal(RichText(response.BodyDoc), RichText(listed.BodyDoc));
    }

    private async Task<TemplateResponse> ListedAs(ContentTemplate template)
    {
        SignIn(PermissionKeys.ContentStudioView);
        _templates.ListAsync(default, default, default, default, default).ReturnsForAnyArgs([template]);
        return Assert.Single(await _service.ListAsync(new TemplateListQuery(), Ct));
    }

    private static string RichText(System.Text.Json.JsonElement? doc) =>
        Platform.Content.Features.Posts.RichTextDocument.ToPlainText(doc!.Value);

    [Fact]
    public async Task Create_ChecksEveryField()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var bad = new SaveTemplateRequest { Name = "", ContentType = "podcast", Description = new string('d', 501), Hashtags = ["two words"] };

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(bad, Ct));

        foreach (var field in new[] { "workspaceId", "name", "description", "contentType", "content", "hashtags" })
        {
            Assert.Contains(field, failure.Errors!.Keys);
        }

        await _templates.DidNotReceiveWithAnyArgs().InsertAsync(default!, default);
    }

    [Fact]
    public async Task Create_RefusesABadDocument()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var request = new SaveTemplateRequest
        {
            WorkspaceId = TestData.WorkspaceId, Name = "x", ContentType = "social_post", Title = "t",
            BodyDoc = TestData.Doc("""{"type":"doc","content":[{"type":"script"}]}""")
        };

        var failure = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(request, Ct));

        Assert.Contains("bodyDoc", failure.Errors!.Keys);
    }

    [Fact]
    public async Task Create_InAWorkspaceThatIsNotTheirs_IsNotFound_AndADuplicateNameIsAConflict()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(false);
        Assert.Equal("workspace_not_found", (await Assert.ThrowsAsync<NotFoundException>(() => _service.CreateAsync(Valid(), Ct))).Code);

        Lookups.WorkspaceExistsAsync(default, default, default, default).ReturnsForAnyArgs(true);
        _templates.InsertAsync(default!, default).ReturnsForAnyArgs(false);
        Assert.Equal("template_exists", (await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync(Valid(), Ct))).Code);
    }

    [Fact]
    public async Task List_ChecksTheType_AndPassesTheFiltersOn()
    {
        SignIn(PermissionKeys.ContentStudioView);
        _templates.ListAsync(default, default, default, default, default).ReturnsForAnyArgs([]);

        await Assert.ThrowsAsync<BadRequestException>(() => _service.ListAsync(new TemplateListQuery(Type: "podcast"), Ct));
        await _service.ListAsync(new TemplateListQuery(TestData.WorkspaceId, "blog_post"), Ct);

        await _templates.Received(1).ListAsync(TestData.AgencyId, TestData.UserId, TestData.WorkspaceId, "blog_post", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ATemplateWithDamagedData_StillListsWithItsPlainText()
    {
        SignIn(PermissionKeys.ContentStudioView);
        var broken = new ContentTemplate
        {
            Id = Guid.NewGuid(), WorkspaceId = TestData.WorkspaceId, Name = "Old", ContentType = "social_post",
            TitleTemplate = "Old title", BodyTemplate = "Plain text", TemplateData = "not json"
        };
        _templates.ListAsync(default, default, default, default, default).ReturnsForAnyArgs([broken]);

        var template = Assert.Single(await _service.ListAsync(new TemplateListQuery(), Ct));

        Assert.Equal(("Plain text", null, 0), (template.BodyText, template.BodyDoc, template.Hashtags.Count));
    }

    [Fact]
    public async Task Delete_OfATemplateThatIsNotThere_IsNotFound()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        _templates.DeleteAsync(default, default, default, default).ReturnsForAnyArgs(false);

        var failure = await Assert.ThrowsAsync<NotFoundException>(() => _service.DeleteAsync(Guid.NewGuid(), Ct));

        Assert.Equal("template_not_found", failure.Code);
    }

    [Fact]
    public async Task Delete_OfATemplate_AsksForItInTheCallersOrganization()
    {
        SignIn(PermissionKeys.ContentStudioCreate);
        var id = Guid.NewGuid();
        _templates.DeleteAsync(default, default, default, default).ReturnsForAnyArgs(true);

        await _service.DeleteAsync(id, Ct);

        await _templates.Received(1).DeleteAsync(TestData.AgencyId, TestData.UserId, id, Arg.Any<CancellationToken>());
    }
}
