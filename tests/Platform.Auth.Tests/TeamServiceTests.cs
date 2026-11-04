using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services;
using Platform.Auth.Settings;
using Platform.Auth.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;
using Platform.Helpers.Tenancy;
using Platform.Helpers.Utilities;
using Platform.Infrastructure.Data;

namespace Platform.Auth.Tests;

public sealed class TeamServiceTests
{
    private readonly ITeamRepository _team = Substitute.For<ITeamRepository>();
    private readonly IAgencyRepository _agencies = Substitute.For<IAgencyRepository>();
    private readonly ISessionRevocationService _revocations = Substitute.For<ISessionRevocationService>();
    private readonly TenantContext _tenant = new();
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly InvitationSettings _settings = new() { InvitationDays = 7 };
    private readonly TeamService _service;

    private readonly Guid _agencyId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public TeamServiceTests()
    {
        GivenBusinessModel(BusinessModel.Agency);
        GivenManager();
        _service = new TeamService(_team, _agencies, _revocations, _tenant, Options.Create(_settings), _time, NullLogger<TeamService>.Instance);
    }

    private void GivenBusinessModel(BusinessModel model) =>
        _agencies.GetAsync(_agencyId, _userId, Arg.Any<CancellationToken>())
            .Returns(new AgencyRecord { Id = _agencyId, Name = "Acme", BusinessModel = model, Plan = AgencyPlan.Trial });

    /// <summary>The caller holds members.manage, the way an organization admin does by default.</summary>
    private void GivenManager(string role = "organizationadmin") =>
        _tenant.Set(_agencyId, _userId, role, [PermissionKeys.MembersManage]);

    // --- Who may -----------------------------------------------------------------------------

    [Theory]
    [InlineData("organizationadmin")] // the role that holds the permission by default, here without it
    [InlineData("marketingmanager")]
    [InlineData("contentwriter")]
    [InlineData("clientmanager")]
    [InlineData(null)]
    public async Task EveryOperation_WithoutThePermission_IsForbidden_WhateverTheRole_AndTouchesNothing(string? role)
    {
        // Everything else the catalog has, including organization.manage that used to stand for "manage".
        _tenant.Set(_agencyId, _userId, role, PermissionKeys.All.Where(key => key != PermissionKeys.MembersManage));
        var someone = Guid.NewGuid();

        Func<Task>[] operations =
        [
            () => _service.GetTeamAsync(CancellationToken.None),
            () => _service.InviteAsync(new InviteMemberRequest("new@example.com", "contentwriter"), CancellationToken.None),
            () => _service.RevokeInvitationAsync(someone, CancellationToken.None),
            () => _service.ChangeRoleAsync(someone, new ChangeMemberRoleRequest("contentwriter"), CancellationToken.None),
            () => _service.RemoveMemberAsync(someone, CancellationToken.None)
        ];

        foreach (var operation in operations)
        {
            var exception = await Assert.ThrowsAsync<ForbiddenException>(operation);
            Assert.Equal("insufficient_permission", exception.Code);
        }

        Assert.Empty(_team.ReceivedCalls());
        Assert.Empty(_revocations.ReceivedCalls());
    }

    [Fact]
    public async Task ARoleTheSiteAdminGaveThePermission_MayInvite_BecauseThePermissionNotTheRoleDecides()
    {
        GivenManager("marketingmanager");

        await _service.InviteAsync(new InviteMemberRequest("new@example.com", "contentwriter"), CancellationToken.None);

        await _team.Received(1).InsertInvitationAsync(Arg.Any<NewInvitation>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WithoutASignedInUserOrOrganization_EveryOperationIsUnauthorized()
    {
        _tenant.Set(null, null, null);

        await Assert.ThrowsAsync<UnauthorizedException>(() => _service.GetTeamAsync(CancellationToken.None));
        await Assert.ThrowsAsync<UnauthorizedException>(
            () => _service.InviteAsync(new InviteMemberRequest("new@example.com", "contentwriter"), CancellationToken.None));
        Assert.Empty(_team.ReceivedCalls());
    }

    // --- The team ----------------------------------------------------------------------------

    [Fact]
    public async Task GetTeam_ListsThePeopleAndTheWaitingInvitations_OfTheCallersOrganization()
    {
        var other = Guid.NewGuid();
        _team.ListMembersAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns(
        [
            new TeamMemberRecord { UserId = _userId, Email = "me@example.com", FullName = "Me", Role = OrganizationRole.OrganizationAdmin, JoinedAt = _time.Now },
            new TeamMemberRecord { UserId = other, Email = "them@example.com", FullName = "Them", Role = OrganizationRole.ContentWriter, JoinedAt = _time.Now.AddDays(1) }
        ]);
        _team.ListPendingInvitationsAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns(
        [
            new InvitationRecord { Id = Guid.NewGuid(), Email = "live@example.com", Role = OrganizationRole.MarketingManager, CreatedAt = _time.Now, ExpiresAt = _time.Now.AddDays(1) },
            new InvitationRecord { Id = Guid.NewGuid(), Email = "old@example.com", Role = OrganizationRole.ContentWriter, CreatedAt = _time.Now.AddDays(-8), ExpiresAt = _time.Now.AddDays(-1) }
        ]);

        var team = await _service.GetTeamAsync(CancellationToken.None);

        Assert.Equal([true, false], team.Members.Select(m => m.IsYou));
        Assert.Equal(["me@example.com", "them@example.com"], team.Members.Select(m => m.Email));
        Assert.Equal([false, true], team.Invitations.Select(i => i.IsExpired));
        await _team.Received(1).ListMembersAsync(_agencyId, _userId, Arg.Any<CancellationToken>());
    }

    // --- Inviting ----------------------------------------------------------------------------

    [Theory]
    [InlineData(BusinessModel.Company, "marketingmanager")]
    [InlineData(BusinessModel.Company, "organizationadmin")]
    [InlineData(BusinessModel.Agency, "clientmanager")]
    [InlineData(BusinessModel.Agency, "contentwriter")]
    public async Task Invite_StoresOnlyTheHashOfTheToken_AndReturnsTheTokenOnce(BusinessModel model, string role)
    {
        GivenBusinessModel(model);
        NewInvitation? stored = null;
        await _team.InsertInvitationAsync(Arg.Do<NewInvitation>(i => stored = i), Arg.Any<CancellationToken>());

        var created = await _service.InviteAsync(new InviteMemberRequest("  New.Person@Example.COM ", role), CancellationToken.None);

        Assert.NotNull(stored);
        Assert.Equal(_agencyId, stored.AgencyId);
        Assert.Equal(_userId, stored.InvitedBy);
        Assert.Equal(model, stored.BusinessModel);
        Assert.Equal("new.person@example.com", stored.Email);
        Assert.Equal(role, stored.Role.ToDb());
        Assert.Equal(_time.Now, stored.CreatedAt);
        Assert.Equal(_time.Now.AddDays(7), stored.ExpiresAt);

        // The secret leaves once, in the response; what is stored is its hash and nothing that opens the link.
        Assert.True(created.Token.Length >= 40, "The token must be long enough to be unguessable.");
        Assert.Equal(HashHelper.Sha256Hex(created.Token), stored.TokenHash);
        Assert.NotEqual(created.Token, stored.TokenHash);
        Assert.DoesNotContain(created.Token, stored.ToString());
        Assert.Equal(new InvitationResponse(stored.Id, "new.person@example.com", stored.Role, _time.Now, _time.Now.AddDays(7), IsExpired: false), created.Invitation);
    }

    [Fact]
    public async Task Invite_UsesTheConfiguredLifetime()
    {
        _settings.InvitationDays = 3;
        NewInvitation? stored = null;
        await _team.InsertInvitationAsync(Arg.Do<NewInvitation>(i => stored = i), Arg.Any<CancellationToken>());

        await _service.InviteAsync(new InviteMemberRequest("new@example.com", "contentwriter"), CancellationToken.None);

        Assert.Equal(_time.Now.AddDays(3), stored!.ExpiresAt);
    }

    [Fact]
    public async Task Invite_MakesANewTokenEachTime()
    {
        var first = await _service.InviteAsync(new InviteMemberRequest("one@example.com", "contentwriter"), CancellationToken.None);
        var second = await _service.InviteAsync(new InviteMemberRequest("two@example.com", "contentwriter"), CancellationToken.None);

        Assert.NotEqual(first.Token, second.Token);
        Assert.NotEqual(first.Invitation.Id, second.Invitation.Id);
    }

    [Fact]
    public async Task Invite_ForAnIndividualCreator_IsAConflict_EvenForAHolderOfThePermission()
    {
        GivenBusinessModel(BusinessModel.Creator);

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.InviteAsync(new InviteMemberRequest("new@example.com", "contentwriter"), CancellationToken.None));

        Assert.Equal("team_not_available", exception.Code);
        await _team.DidNotReceiveWithAnyArgs().InsertInvitationAsync(default!, default);
    }

    [Theory]
    [InlineData(BusinessModel.Company, "clientmanager")] // only an agency has client managers
    public async Task Invite_WithARoleTheModelDoesNotHave_IsABadRequestOnTheRoleField(BusinessModel model, string role)
    {
        GivenBusinessModel(model);

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.InviteAsync(new InviteMemberRequest("new@example.com", role), CancellationToken.None));

        Assert.Contains("role", exception.Errors!.Keys);
        await _team.DidNotReceiveWithAnyArgs().InsertInvitationAsync(default!, default);
    }

    [Fact]
    public async Task Invite_ForAnAddressThatIsAlreadyAnAccount_IsAConflict_AndStoresNothing()
    {
        _team.EmailIsRegisteredAsync("taken@example.com", Arg.Any<CancellationToken>()).Returns(true);

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.InviteAsync(new InviteMemberRequest(" Taken@Example.com ", "contentwriter"), CancellationToken.None));

        Assert.Equal("email_taken", exception.Code);
        await _team.DidNotReceiveWithAnyArgs().InsertInvitationAsync(default!, default);
    }

    [Fact]
    public async Task Invite_WhileAnotherIsWaiting_PassesTheRepositorysConflictOn()
    {
        _team.InsertInvitationAsync(Arg.Any<NewInvitation>(), Arg.Any<CancellationToken>())
            .Throws(new ConflictException("An invitation for this address is already waiting.", "invitation_exists"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.InviteAsync(new InviteMemberRequest("new@example.com", "contentwriter"), CancellationToken.None));

        Assert.Equal("invitation_exists", exception.Code);
    }

    [Theory]
    [InlineData(null, "contentwriter", "email")]
    [InlineData("not an email", "contentwriter", "email")]
    [InlineData("new@example.com", null, "role")]
    [InlineData("new@example.com", "boss", "role")]
    [InlineData("new@example.com", "OrganizationAdmin", "role")] // the spelling is the lower-case one
    public async Task Invite_WithABadRequest_ReportsTheField_AndStoresNothing(string? email, string? role, string field)
    {
        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.InviteAsync(new InviteMemberRequest(email, role), CancellationToken.None));

        Assert.Contains(field, exception.Errors!.Keys);
        Assert.Empty(_team.ReceivedCalls());
    }

    [Fact]
    public async Task Invite_WhenTheOrganizationCannotBeSeen_IsUnauthorized()
    {
        _agencies.GetAsync(_agencyId, _userId, Arg.Any<CancellationToken>()).Returns((AgencyRecord?)null);

        var exception = await Assert.ThrowsAsync<UnauthorizedException>(
            () => _service.InviteAsync(new InviteMemberRequest("new@example.com", "contentwriter"), CancellationToken.None));

        Assert.Equal("membership_ended", exception.Code);
        await _team.DidNotReceiveWithAnyArgs().InsertInvitationAsync(default!, default);
    }

    // --- Revoking ----------------------------------------------------------------------------

    [Fact]
    public async Task Revoke_AsksTheRepositoryForTheCallersOrganizationOnly()
    {
        var invitation = Guid.NewGuid();
        _team.RevokeInvitationAsync(_agencyId, _userId, invitation, _time.Now, Arg.Any<CancellationToken>()).Returns(true);

        await _service.RevokeInvitationAsync(invitation, CancellationToken.None);

        await _team.Received(1).RevokeInvitationAsync(_agencyId, _userId, invitation, _time.Now, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Revoke_OfAnInvitationThatIsNotWaiting_IsNotFound()
    {
        var exception = await Assert.ThrowsAsync<NotFoundException>(
            () => _service.RevokeInvitationAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Equal("invitation_not_found", exception.Code);
    }

    // --- Changing a role ---------------------------------------------------------------------

    [Fact]
    public async Task ChangeRole_PassesTheRoleOn_AndMakesTheEndedSessionVisibleAtOnce()
    {
        var member = Guid.NewGuid();

        await _service.ChangeRoleAsync(member, new ChangeMemberRoleRequest("marketingmanager"), CancellationToken.None);

        Received.InOrder(() =>
        {
            _team.ChangeRoleAsync(_agencyId, _userId, member, OrganizationRole.MarketingManager, Arg.Any<CancellationToken>());
            _revocations.Invalidate();
        });
    }

    [Fact]
    public async Task ChangeRole_ForgetsWhatIsHeldEvenWhenTheAttemptFailed()
    {
        _team.ChangeRoleAsync(default, default, default, default, default)
            .ThrowsForAnyArgs(new ConflictException("An organization needs at least one organization admin.", "last_organization_admin"));

        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ChangeRoleAsync(Guid.NewGuid(), new ChangeMemberRoleRequest("contentwriter"), CancellationToken.None));

        Assert.Equal("last_organization_admin", exception.Code);
        _revocations.Received(1).Invalidate();
    }

    [Fact]
    public async Task ChangeRole_OfYourself_IsAConflict_WithoutAskingTheDatabase()
    {
        var exception = await Assert.ThrowsAsync<ConflictException>(
            () => _service.ChangeRoleAsync(_userId, new ChangeMemberRoleRequest("contentwriter"), CancellationToken.None));

        Assert.Equal("cannot_change_self", exception.Code);
        await _team.DidNotReceiveWithAnyArgs().ChangeRoleAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task ChangeRole_ToARoleTheModelDoesNotHave_IsABadRequest_AndChangesNothing()
    {
        GivenBusinessModel(BusinessModel.Company);

        var exception = await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ChangeRoleAsync(Guid.NewGuid(), new ChangeMemberRoleRequest("clientmanager"), CancellationToken.None));

        Assert.Contains("role", exception.Errors!.Keys);
        await _team.DidNotReceiveWithAnyArgs().ChangeRoleAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task ChangeRole_WithoutARole_IsABadRequest()
    {
        await Assert.ThrowsAsync<BadRequestException>(
            () => _service.ChangeRoleAsync(Guid.NewGuid(), new ChangeMemberRoleRequest(null), CancellationToken.None));
    }

    // --- Removing ----------------------------------------------------------------------------

    [Fact]
    public async Task Remove_PassesItOn_AndMakesTheEndedSessionVisibleAtOnce()
    {
        var member = Guid.NewGuid();

        await _service.RemoveMemberAsync(member, CancellationToken.None);

        Received.InOrder(() =>
        {
            _team.RemoveMemberAsync(_agencyId, _userId, member, Arg.Any<CancellationToken>());
            _revocations.Invalidate();
        });
    }

    [Fact]
    public async Task Remove_ForgetsWhatIsHeldEvenWhenTheAttemptFailed()
    {
        _team.RemoveMemberAsync(default, default, default, default)
            .ThrowsForAnyArgs(new NotFoundException("That person is not a member of this organization.", "member_not_found"));

        await Assert.ThrowsAsync<NotFoundException>(() => _service.RemoveMemberAsync(Guid.NewGuid(), CancellationToken.None));

        _revocations.Received(1).Invalidate();
    }

    [Fact]
    public async Task Remove_OfYourself_IsAConflict_WithoutAskingTheDatabase()
    {
        var exception = await Assert.ThrowsAsync<ConflictException>(() => _service.RemoveMemberAsync(_userId, CancellationToken.None));

        Assert.Equal("cannot_change_self", exception.Code);
        await _team.DidNotReceiveWithAnyArgs().RemoveMemberAsync(default, default, default, default);
        _revocations.DidNotReceive().Invalidate();
    }
}
