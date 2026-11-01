using Platform.Auth.DTOs;
using Platform.Helpers.Exceptions;

namespace Platform.Auth.Tests;

public sealed class RequestValidationTests
{
    private const string GoodPassword = "ten chars!!";

    private static RegisterRequest GoodRegistration() => new("ana@example.com", GoodPassword, "Ana", "Acme", "Client one");

    private static string[] Fields(Platform.Helpers.Validation.ValidationErrors errors) => [.. errors.ToDictionary().Keys.Order()];

    // --- Register ----------------------------------------------------------------------------

    [Fact]
    public void Register_WithGoodInput_IsValid()
    {
        Assert.False(GoodRegistration().Validate().Any);
    }

    [Fact]
    public void Register_OptionalFieldsMayBeMissing()
    {
        Assert.False(new RegisterRequest("ana@example.com", GoodPassword, null, "Acme", null).Validate().Any);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no-at-sign")]
    [InlineData("two@@example.com")]
    [InlineData("spaces in@example.com")]
    [InlineData("missing-domain@")]
    [InlineData("Display Name <ana@example.com>")]
    public void Register_WithABadEmail_ReportsTheEmailField(string? email)
    {
        var errors = (GoodRegistration() with { Email = email }).Validate();

        Assert.Equal(["email"], Fields(errors));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    public void Register_WithAMissingOrShortPassword_ReportsThePasswordField(string? password)
    {
        var errors = (GoodRegistration() with { Password = password }).Validate();

        Assert.Equal(["password"], Fields(errors));
    }

    [Fact]
    public void Register_PasswordOfExactlyTenCharacters_IsAccepted()
    {
        Assert.False((GoodRegistration() with { Password = "0123456789" }).Validate().Any);
    }

    [Fact]
    public void Register_PasswordLongerThan128Characters_IsRejected()
    {
        Assert.Equal(["password"], Fields((GoodRegistration() with { Password = new string('p', 129) }).Validate()));
        Assert.False((GoodRegistration() with { Password = new string('p', 128) }).Validate().Any);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_WithoutAnAgencyName_ReportsTheAgencyNameField(string? name)
    {
        Assert.Equal(["agencyName"], Fields((GoodRegistration() with { AgencyName = name }).Validate()));
    }

    [Fact]
    public void Register_NamesLongerThan200Characters_AreRejected()
    {
        var errors = (GoodRegistration() with
        {
            FullName = new string('n', 201),
            AgencyName = new string('a', 201),
            WorkspaceName = new string('w', 201)
        }).Validate();

        Assert.Equal(["agencyName", "fullName", "workspaceName"], Fields(errors));
    }

    [Fact]
    public void Register_ReportsEveryProblemAtOnce()
    {
        var errors = new RegisterRequest("nope", "short", null, "", null).Validate();

        Assert.Equal(["agencyName", "email", "password"], Fields(errors));
    }

    [Fact]
    public void Register_ThrowIfAny_RaisesABadRequestWithFieldMessages()
    {
        var exception = Assert.Throws<BadRequestException>(() => new RegisterRequest("nope", GoodPassword, null, "Acme", null).Validate().ThrowIfAny());

        Assert.Equal(400, exception.StatusCode);
        Assert.Equal("validation_failed", exception.Code);
        Assert.Equal("Enter a valid email address.", exception.Errors!["email"].Single());
    }

    // --- Login -------------------------------------------------------------------------------

    [Fact]
    public void Login_WithEmailAndPassword_IsValid_WhateverThePasswordRules()
    {
        // Login never judges a password; it only checks one was sent.
        Assert.False(new LoginRequest("ana@example.com", "x", null).Validate().Any);
    }

    [Theory]
    [InlineData(null, "password")]
    [InlineData("not an email", "password")]
    public void Login_WithABadEmail_ReportsTheEmailField(string? email, string password)
    {
        Assert.Equal(["email"], Fields(new LoginRequest(email, password, null).Validate()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Login_WithoutAPassword_ReportsThePasswordField(string? password)
    {
        Assert.Equal(["password"], Fields(new LoginRequest("ana@example.com", password, null).Validate()));
    }

    [Fact]
    public void Login_WithAPasswordOver128Characters_IsRejected()
    {
        Assert.Equal(["password"], Fields(new LoginRequest("ana@example.com", new string('p', 129), null).Validate()));
    }

    // --- Switch agency and workspaces --------------------------------------------------------

    [Fact]
    public void SwitchAgency_NeedsARealAgencyId()
    {
        Assert.False(new SwitchAgencyRequest(Guid.NewGuid()).Validate().Any);
        Assert.Equal(["agencyId"], Fields(new SwitchAgencyRequest(null).Validate()));
        Assert.Equal(["agencyId"], Fields(new SwitchAgencyRequest(Guid.Empty).Validate()));
    }

    // --- Site admin --------------------------------------------------------------------------

    [Fact]
    public void SiteLogin_WithEmailAndPassword_IsValid()
    {
        Assert.False(new SiteLoginRequest("boss@example.com", "x").Validate().Any);
    }

    [Fact]
    public void SiteLogin_ReportsEveryBadField()
    {
        Assert.Equal(["email", "password"], Fields(new SiteLoginRequest("nope", "").Validate()));
        Assert.Equal(["email", "password"], Fields(new SiteLoginRequest(null, null).Validate()));
    }

    [Fact]
    public void SiteLogin_WithAPasswordOver128Characters_IsRejected()
    {
        Assert.Equal(["password"], Fields(new SiteLoginRequest("boss@example.com", new string('p', 129)).Validate()));
    }

    [Fact]
    public void SiteAgenciesQuery_DefaultsToTheFirstPageOfTwenty_AndIsValid()
    {
        var query = new SiteAgenciesQuery();

        Assert.Equal(1, query.Page);
        Assert.Equal(20, query.PageSize);
        Assert.False(query.Validate().Any);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 100)]
    [InlineData(500, 50)]
    public void SiteAgenciesQuery_WithinTheLimits_IsValid(int page, int pageSize)
    {
        Assert.False(new SiteAgenciesQuery(page, pageSize).Validate().Any);
    }

    [Theory]
    [InlineData(0, 20, "page")]
    [InlineData(-3, 20, "page")]
    [InlineData(1, 0, "pageSize")]
    [InlineData(1, 101, "pageSize")]
    public void SiteAgenciesQuery_OutsideTheLimits_ReportsTheBadField(int page, int pageSize, string field)
    {
        Assert.Equal([field], Fields(new SiteAgenciesQuery(page, pageSize).Validate()));
    }

    [Theory]
    [InlineData("Client one", false)]
    [InlineData("x", false)]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("    ", true)]
    public void CreateWorkspace_NeedsAName(string? name, bool invalid)
    {
        Assert.Equal(invalid, new CreateWorkspaceRequest(name).Validate().Any);
    }

    [Fact]
    public void CreateWorkspace_NameIsLimitedTo200Characters_CountedAfterTrimming()
    {
        Assert.False(new CreateWorkspaceRequest(" " + new string('n', 200) + " ").Validate().Any);
        Assert.True(new CreateWorkspaceRequest(new string('n', 201)).Validate().Any);
    }
}
