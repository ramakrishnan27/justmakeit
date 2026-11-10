using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Tests;

public sealed class SiteChannelServiceTests
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 5, 12, 0, 0, TimeSpan.Zero);
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4];

    private readonly ISiteChannelRepository _channels = Substitute.For<ISiteChannelRepository>();
    private readonly TenantContext _tenant = new();
    private readonly SiteChannelService _service;
    private readonly Guid _userId = Guid.NewGuid();

    public SiteChannelServiceTests()
    {
        _service = new SiteChannelService(_channels, _tenant, NullLogger<SiteChannelService>.Instance);
    }

    private void SignInAsSiteAdmin() => _tenant.Set(null, _userId, RoleNames.SiteAdmin);

    private static SiteChannelRecord Record(string name = "Telegram", string? iconKey = "broadcast", bool hasImage = false) =>
        new()
        {
            Id = Guid.NewGuid(), Name = name, Description = "Chat", IconKey = iconKey, HasImage = hasImage,
            CreatedByName = "Root", CreatedAt = Noon, OrganizationCount = 2
        };

    // --- List --------------------------------------------------------------------------------

    [Fact]
    public async Task List_MapsEveryField()
    {
        SignInAsSiteAdmin();
        var record = Record("Instagram", "instagram");
        _channels.ListAsync(_userId, Arg.Any<CancellationToken>()).Returns([record]);

        var list = await _service.ListAsync(CancellationToken.None);

        Assert.Equal([new SiteChannelResponse(record.Id, "Instagram", "Chat", "instagram", false, "Root", Noon, 2)], list);
    }

    // --- Create ------------------------------------------------------------------------------

    [Fact]
    public async Task Create_WithNoImage_GetsTheDefaultGlyph_AndTrimmedText()
    {
        SignInAsSiteAdmin();
        NewChannel? sent = null;
        _channels.CreateAsync(_userId, Arg.Do<NewChannel>(c => sent = c), Arg.Any<CancellationToken>()).Returns(Record());

        await _service.CreateAsync("  Telegram  ", "  Fast chat  ", null, null, CancellationToken.None);

        Assert.Equal(new NewChannel("Telegram", "Fast chat", "broadcast", null, null), sent);
    }

    [Fact]
    public async Task Create_WithAnImage_StoresTheImageAndTheTypeFoundInIt_AndNoGlyph()
    {
        SignInAsSiteAdmin();
        NewChannel? sent = null;
        _channels.CreateAsync(_userId, Arg.Do<NewChannel>(c => sent = c), Arg.Any<CancellationToken>()).Returns(Record(iconKey: null, hasImage: true));

        var created = await _service.CreateAsync("Telegram", null, Png, "image/png", CancellationToken.None);

        Assert.Equal("Telegram", sent!.Name);
        Assert.Equal(string.Empty, sent.Description);
        Assert.Null(sent.IconKey);
        Assert.Equal(Png, sent.Image);
        Assert.Equal("image/png", sent.ContentType);
        Assert.True(created.HasIconImage);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutAName_IsABadRequest_AndStoresNothing(string? name)
    {
        SignInAsSiteAdmin();

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(name, null, null, null, CancellationToken.None));

        Assert.Contains("name", exception.Errors!.Keys);
        await _channels.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default);
    }

    [Fact]
    public async Task Create_WithATooLongNameOrDescription_OrControlCharacters_IsABadRequest()
    {
        SignInAsSiteAdmin();

        var longName = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync(new string('a', 61), null, null, null, CancellationToken.None));
        var longDescription = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync("Telegram", new string('a', 301), null, null, CancellationToken.None));
        var lineBreakName = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync("Tele\ngram", null, null, null, CancellationToken.None));
        var lineBreakDescription = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync("Telegram", "one\r\ntwo", null, null, CancellationToken.None));

        Assert.Contains("name", longName.Errors!.Keys);
        Assert.Contains("description", longDescription.Errors!.Keys);
        Assert.Contains("name", lineBreakName.Errors!.Keys);
        Assert.Contains("description", lineBreakDescription.Errors!.Keys);
        await _channels.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default);
    }

    [Fact]
    public async Task Create_AcceptsTheLongestNameAndDescription()
    {
        SignInAsSiteAdmin();
        _channels.CreateAsync(_userId, Arg.Any<NewChannel>(), Arg.Any<CancellationToken>()).Returns(Record());

        await _service.CreateAsync(new string('a', 60), new string('b', 300), null, null, CancellationToken.None);

        await _channels.Received(1).CreateAsync(_userId, Arg.Any<NewChannel>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Create_WithAnIconThatIsNotAnImage_IsABadRequestOnTheIconField_AndStoresNothing()
    {
        SignInAsSiteAdmin();
        var svg = System.Text.Encoding.UTF8.GetBytes("<svg onload=\"alert(1)\"></svg>");

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync("Telegram", null, svg, "image/svg+xml", CancellationToken.None));

        Assert.Equal(["icon"], exception.Errors!.Keys);
        await _channels.DidNotReceiveWithAnyArgs().CreateAsync(default, default!, default);
    }

    [Fact]
    public async Task Create_ReportsEveryProblemAtOnce()
    {
        SignInAsSiteAdmin();

        var exception = await Assert.ThrowsAsync<BadRequestException>(() => _service.CreateAsync("", new string('x', 301), [1, 2, 3], "image/png", CancellationToken.None));

        Assert.Equivalent(new[] { "name", "description", "icon" }, exception.Errors!.Keys);
    }

    [Fact]
    public async Task Create_WithATakenName_PassesTheConflictOn()
    {
        SignInAsSiteAdmin();
        _channels.CreateAsync(_userId, Arg.Any<NewChannel>(), Arg.Any<CancellationToken>())
            .Returns<SiteChannelRecord>(_ => throw new ConflictException("A channel with that name already exists.", "channel_exists"));

        var exception = await Assert.ThrowsAsync<ConflictException>(() => _service.CreateAsync("Email", null, null, null, CancellationToken.None));

        Assert.Equal("channel_exists", exception.Code);
    }

    // --- Icon --------------------------------------------------------------------------------

    [Fact]
    public async Task Icon_ReturnsTheStoredImage()
    {
        SignInAsSiteAdmin();
        var id = Guid.NewGuid();
        _channels.GetIconAsync(_userId, id, Arg.Any<CancellationToken>()).Returns(new ChannelIconRecord { ContentType = "image/png", Image = Png });

        var icon = await _service.GetIconAsync(id, CancellationToken.None);

        Assert.Equal("image/png", icon.ContentType);
        Assert.Equal(Png, icon.Image);
    }

    [Fact]
    public async Task Icon_OfAChannelWithNoImage_IsNotFound()
    {
        SignInAsSiteAdmin();
        _channels.GetIconAsync(_userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ChannelIconRecord?)null);

        var exception = await Assert.ThrowsAsync<NotFoundException>(() => _service.GetIconAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("channel_not_found", exception.Code);
    }

    // --- One organization's channels ---------------------------------------------------------

    [Fact]
    public async Task OrganizationChannels_AreListedWithWhetherEachIsEnabled()
    {
        SignInAsSiteAdmin();
        var agency = Guid.NewGuid();
        var first = new OrganizationChannelRecord { Id = Guid.NewGuid(), Name = "Email", Description = "Mail", IconKey = "email", HasImage = false, IsEnabled = true };
        var second = new OrganizationChannelRecord { Id = Guid.NewGuid(), Name = "Telegram", Description = "", IconKey = null, HasImage = true, IsEnabled = false };
        _channels.ListForOrganizationAsync(_userId, agency, Arg.Any<CancellationToken>()).Returns([first, second]);

        var list = await _service.ListForOrganizationAsync(agency, CancellationToken.None);

        Assert.Equal(
            [new OrganizationChannelResponse(first.Id, "Email", "Mail", "email", false, true),
             new OrganizationChannelResponse(second.Id, "Telegram", "", null, true, false)],
            list);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SettingAChannel_PassesTheChoiceToTheDatabase(bool enabled)
    {
        SignInAsSiteAdmin();
        var agency = Guid.NewGuid();
        var channel = Guid.NewGuid();

        await _service.SetForOrganizationAsync(agency, channel, new SetOrganizationChannelRequest(enabled), CancellationToken.None);

        await _channels.Received(1).SetForOrganizationAsync(_userId, agency, channel, enabled, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SettingAChannel_WithoutSayingEnabledOrNot_IsABadRequest_AndChangesNothing()
    {
        SignInAsSiteAdmin();

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.SetForOrganizationAsync(Guid.NewGuid(), Guid.NewGuid(), new SetOrganizationChannelRequest(null), CancellationToken.None));

        Assert.Contains("enabled", exception.Errors!.Keys);
        await _channels.DidNotReceiveWithAnyArgs().SetForOrganizationAsync(default, default, default, default, default);
    }

    // --- Who may ------------------------------------------------------------------------------

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("marketingmanager")]
    [InlineData("")]
    [InlineData(null)]
    public async Task EveryOperation_ForAnyTokenThatIsNotASiteAdminToken_IsForbidden_BeforeTheDatabaseIsAsked(string? role)
    {
        _tenant.Set(Guid.NewGuid(), _userId, role);

        Task[] calls =
        [
            _service.ListAsync(CancellationToken.None),
            _service.CreateAsync("Telegram", null, null, null, CancellationToken.None),
            _service.GetIconAsync(Guid.NewGuid(), CancellationToken.None),
            _service.ListForOrganizationAsync(Guid.NewGuid(), CancellationToken.None),
            _service.SetForOrganizationAsync(Guid.NewGuid(), Guid.NewGuid(), new SetOrganizationChannelRequest(true), CancellationToken.None)
        ];

        foreach (var call in calls)
        {
            var exception = await Assert.ThrowsAsync<ForbiddenException>(() => call);
            Assert.Equal("not_site_admin", exception.Code);
        }

        Assert.Empty(_channels.ReceivedCalls());
    }

    [Fact]
    public async Task WithoutASignedInUser_EveryOperationIsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ListAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.CreateAsync("Telegram", null, null, null, CancellationToken.None));
    }
}
