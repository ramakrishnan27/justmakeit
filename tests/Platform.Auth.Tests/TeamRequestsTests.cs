using Microsoft.Extensions.Options;
using Platform.Auth.DTOs;
using Platform.Auth.Settings;

namespace Platform.Auth.Tests;

public sealed class TeamRequestsTests
{
    private static string[] Fields(Platform.Helpers.Validation.ValidationErrors errors) => [.. errors.ToDictionary().Keys.Order()];

    // --- Invite ------------------------------------------------------------------------------

    [Theory]
    [InlineData("organizationadmin")]
    [InlineData("clientmanager")]
    [InlineData("marketingmanager")]
    [InlineData("contentwriter")]
    public void Invite_WithEachRoleName_IsValid(string role) =>
        Assert.False(new InviteMemberRequest("Test Person", "new@example.com", role).Validate().Any);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("nope")]
    [InlineData("OrganizationAdmin")]
    [InlineData("1")]
    public void Invite_WithAMissingOrUnknownRole_ReportsTheRoleField(string? role) =>
        Assert.Equal(["role"], Fields(new InviteMemberRequest("Test Person", "new@example.com", role).Validate()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("no-at-sign")]
    [InlineData("Display Name <ana@example.com>")]
    public void Invite_WithABadEmail_ReportsTheEmailField(string? email) =>
        Assert.Equal(["email"], Fields(new InviteMemberRequest("Test Person", email, "contentwriter").Validate()));

    [Fact]
    public void Invite_WithNothing_ReportsEveryField() =>
        Assert.Equal(["email", "fullName", "role"], Fields(new InviteMemberRequest(null, null, null).Validate()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Invite_WithoutAName_ReportsTheNameField(string? name) =>
        Assert.Equal(["fullName"], Fields(new InviteMemberRequest(name, "new@example.com", "contentwriter").Validate()));

    [Fact]
    public void Invite_WithANameOfAtMost200Characters_IsValid_AndOneLongerIsNot()
    {
        Assert.False(new InviteMemberRequest(new string('a', 200), "new@example.com", "contentwriter").Validate().Any);
        Assert.Equal(["fullName"], Fields(new InviteMemberRequest(new string('a', 201), "new@example.com", "contentwriter").Validate()));
    }

    // --- Activate an invitation by hand ------------------------------------------------------

    [Theory]
    [InlineData("0123456789")]
    [InlineData("a default password")]
    public void ActivateInvitation_WithAPasswordOf10To128Characters_IsValid(string password) =>
        Assert.False(new ActivateInvitationRequest(password).Validate().Any);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    public void ActivateInvitation_WithAMissingOrShortPassword_ReportsThePasswordField(string? password) =>
        Assert.Equal(["password"], Fields(new ActivateInvitationRequest(password).Validate()));

    [Fact]
    public void ActivateInvitation_WithAPasswordOver128Characters_ReportsThePasswordField() =>
        Assert.Equal(["password"], Fields(new ActivateInvitationRequest(new string('a', 129)).Validate()));

    // --- Deactivate and activate a member ----------------------------------------------------

    [Fact]
    public void SetMemberActive_NeedsAYesOrANo()
    {
        Assert.False(new SetMemberActiveRequest(true).Validate().Any);
        Assert.False(new SetMemberActiveRequest(false).Validate().Any);
        Assert.Equal(["isActive"], Fields(new SetMemberActiveRequest(null).Validate()));
    }

    // --- Change a role -----------------------------------------------------------------------

    [Fact]
    public void ChangeRole_NeedsARole()
    {
        Assert.False(new ChangeMemberRoleRequest("contentwriter").Validate().Any);
        Assert.Equal(["role"], Fields(new ChangeMemberRoleRequest("admin").Validate()));
        Assert.Equal(["role"], Fields(new ChangeMemberRoleRequest(null).Validate()));
    }

    // --- Tokens ------------------------------------------------------------------------------

    [Fact]
    public void ATokenOfTheRightShape_IsValid() =>
        Assert.False(new InvitationTokenRequest(new string('a', 43)).Validate().Any);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void AMissingToken_ReportsTheTokenField(string? token) =>
        Assert.Equal(["token"], Fields(new InvitationTokenRequest(token).Validate()));

    [Fact]
    public void AnAbsurdlyLongToken_IsRefusedBeforeAnythingIsHashed() =>
        Assert.Equal(["token"], Fields(new InvitationTokenRequest(new string('a', MaxTokenLength + 1)).Validate()));

    // The limit lives in an internal class of the DTO project; this is its value (a real token is 43 characters).
    private const int MaxTokenLength = 200;

    // --- Accept ------------------------------------------------------------------------------

    [Fact]
    public void Accept_WithAGoodForm_IsValid() =>
        Assert.False(new AcceptInvitationRequest(new string('a', 43), "Ana Smith", "ten chars!!").Validate().Any);

    [Fact]
    public void Accept_WithNothing_ReportsEveryField() =>
        Assert.Equal(["fullName", "password", "token"], Fields(new AcceptInvitationRequest(null, null, null).Validate()));

    [Fact]
    public void Accept_FollowsTheSamePasswordRulesAsRegistering()
    {
        Assert.Equal(["password"], Fields(new AcceptInvitationRequest(new string('a', 43), "Ana", "123456789").Validate()));
        Assert.Equal(["password"], Fields(new AcceptInvitationRequest(new string('a', 43), "Ana", new string('p', 129)).Validate()));
        Assert.False(new AcceptInvitationRequest(new string('a', 43), "Ana", "0123456789").Validate().Any);
    }

    [Fact]
    public void Accept_WithATooLongName_ReportsTheNameField() =>
        Assert.Equal(["fullName"], Fields(new AcceptInvitationRequest(new string('a', 43), new string('n', 201), "ten chars!!").Validate()));

    // --- Settings ----------------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(7)]
    [InlineData(30)]
    public void TheLifetimeOfAnInvitation_MayBeOneToThirtyDays(int days) =>
        Assert.True(new InvitationSettingsValidator().Validate(null, new InvitationSettings { InvitationDays = days }).Succeeded);

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(31)]
    public void AnythingElse_IsRefused_SoTheApiDoesNotStartWithALinkThatNeverWorksOrNeverEnds(int days)
    {
        ValidateOptionsResult result = new InvitationSettingsValidator().Validate(null, new InvitationSettings { InvitationDays = days });

        Assert.True(result.Failed);
        Assert.Contains("InvitationDays", result.FailureMessage);
    }

    [Fact]
    public void TheDefaultLifetime_IsSevenDays() => Assert.Equal(7, new InvitationSettings().InvitationDays);
}
