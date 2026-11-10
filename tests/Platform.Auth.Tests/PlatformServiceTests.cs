using NSubstitute;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;

namespace Platform.Auth.Tests;

public sealed class PlatformServiceTests
{
    private readonly IPlatformRepository _platforms = Substitute.For<IPlatformRepository>();
    private readonly TenantContext _tenant = new();
    private readonly PlatformService _service;

    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public PlatformServiceTests()
    {
        _service = new PlatformService(_platforms, _tenant);
    }

    private void SignIn(params string[] permissions) => _tenant.Set(_agencyId, _userId, "contentwriter", permissions);

    private static OrganizationChannelRecord Row(string name, bool enabled, string? iconKey = "email", bool hasImage = false) =>
        new() { Id = Guid.NewGuid(), Name = name, Description = "About " + name, IconKey = iconKey, HasImage = hasImage, IsEnabled = enabled };

    // --- List --------------------------------------------------------------------------------

    [Fact]
    public async Task List_AsksForTheOrganizationAndUserInTheTenantContext_AndMapsEveryField()
    {
        SignIn(PermissionKeys.PlatformsView);
        var email = Row("Email", true);
        var custom = Row("Telegram", false, null, true);
        _platforms.ListAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns([email, custom]);

        var list = await _service.ListAsync(CancellationToken.None);

        Assert.Equal(["Email", "Telegram"], list.Select(p => p.Name));
        Assert.Equal([true, false], list.Select(p => p.IsEnabled));
        Assert.Equal([false, true], list.Select(p => p.HasIconImage));
        Assert.Equal("email", list[0].IconKey);
        Assert.Null(list[1].IconKey);
        Assert.Equal(email.Id, list[0].Id);
        Assert.Equal("About Email", list[0].Description);
    }

    [Fact]
    public async Task List_WithoutThePermission_IsForbidden_BeforeTheDatabaseIsAsked()
    {
        SignIn(PermissionKeys.DashboardView, PermissionKeys.WorkspacesView);

        var failure = await Assert.ThrowsAsync<ForbiddenException>(() => _service.ListAsync(CancellationToken.None));

        Assert.Equal("insufficient_permission", failure.Code);
        await _platforms.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    [Fact]
    public async Task List_WithoutAnOrganizationInTheTenantContext_IsUnauthorized()
    {
        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ListAsync(CancellationToken.None));

        await _platforms.DidNotReceiveWithAnyArgs().ListAsync(default, default, default);
    }

    [Fact]
    public async Task List_ForASiteAdminWhoHasNoOrganization_IsUnauthorized()
    {
        _tenant.Set(null, _userId, RoleNames.SiteAdmin);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.ListAsync(CancellationToken.None));
    }

    // --- Icon --------------------------------------------------------------------------------

    [Fact]
    public async Task Icon_ReturnsTheStoredImage()
    {
        SignIn(PermissionKeys.PlatformsView);
        var id = Guid.NewGuid();
        var icon = new ChannelIconRecord { ContentType = "image/png", Image = [1, 2, 3] };
        _platforms.GetIconAsync(_agencyId, _userId, id, Arg.Any<CancellationToken>()).Returns(icon);

        Assert.Same(icon, await _service.GetIconAsync(id, CancellationToken.None));
    }

    [Fact]
    public async Task Icon_OfAPlatformWithNoImage_IsNotFound()
    {
        SignIn(PermissionKeys.PlatformsView);
        _platforms.GetIconAsync(_agencyId, _userId, Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns((ChannelIconRecord?)null);

        var failure = await Assert.ThrowsAsync<NotFoundException>(() => _service.GetIconAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("platform_not_found", failure.Code);
    }

    [Fact]
    public async Task Icon_WithoutThePermission_IsForbidden_BeforeTheDatabaseIsAsked()
    {
        SignIn(PermissionKeys.DashboardView);

        await Assert.ThrowsAsync<ForbiddenException>(() => _service.GetIconAsync(Guid.NewGuid(), CancellationToken.None));

        await _platforms.DidNotReceiveWithAnyArgs().GetIconAsync(default, default, default, default);
    }
}
