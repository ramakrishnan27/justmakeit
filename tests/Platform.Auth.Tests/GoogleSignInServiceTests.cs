using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using System.Web;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Platform.Auth.DTOs;
using Platform.Auth.Interfaces;
using Platform.Auth.Models;
using Platform.Auth.Services.Google;
using Platform.Auth.Settings;
using Platform.Auth.Tests.Support;
using Platform.Helpers.Constants;
using Platform.Helpers.Exceptions;

namespace Platform.Auth.Tests;

public sealed class GoogleSignInServiceTests
{
    private const string ClientId = "client-123.apps.googleusercontent.com";
    private const string Callback = "http://localhost:5080/v1/auth/google/callback";

    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly IGoogleOAuthClient _google = Substitute.For<IGoogleOAuthClient>();
    private readonly IAccountRepository _accounts = Substitute.For<IAccountRepository>();
    private readonly IAuthService _auth = Substitute.For<IAuthService>();
    private readonly GoogleOAuthStateProtector _protector;
    private readonly GoogleSettings _settings = new() { ClientId = ClientId, ClientSecret = "the-client-secret" };
    private readonly GoogleSignInService _service;

    private static readonly AuthResult Session = new("access-token", new DateTimeOffset(2026, 11, 30, 0, 0, 0, TimeSpan.Zero));
    private static readonly GoogleIdentity Maya = new("sub-maya", "Maya.Rao@Gmail.com", true, "Maya Rao");

    public GoogleSignInServiceTests()
    {
        _protector = new GoogleOAuthStateProtector(new EphemeralDataProtectionProvider(), _time);
        _service = new GoogleSignInService(
            _google, _protector, _accounts, _auth, Options.Create(_settings), _time, NullLogger<GoogleSignInService>.Instance);

        _google.ExchangeCodeAsync(default!, default!, default!, default!, default).ReturnsForAnyArgs(Maya);
        _auth.LoginExternalAsync(default, default).ReturnsForAnyArgs(Session);
        _auth.RegisterExternalAsync(default!, default).ReturnsForAnyArgs(Session);
    }

    private static GoogleStartRequest Register(string model = "company", string name = "Northwind Foods") => new("register", model, name);

    private static GoogleStartRequest Login() => new("login", null, null);

    /// <summary>Starts a sign-in and returns what the browser would hold: the state cookie and the state from the address.</summary>
    private (string Cookie, string State, GoogleSignInStart Start) Started(GoogleStartRequest request)
    {
        var start = _service.Begin(request, Callback);
        var query = HttpUtility.ParseQueryString(new Uri(start.AuthorizationUrl).Query);
        return (start.StateCookieValue, query["state"]!, start);
    }

    private Task<GoogleSignInOutcome> CompleteAsync(string? cookie, string? state, string? code = "the-code", string? error = null) =>
        _service.CompleteAsync(new GoogleCallback(code, state, error), cookie, CancellationToken.None);

    // --- Begin -------------------------------------------------------------------------------

    [Fact]
    public void Begin_SendsThePersonToGoogle_WithEverythingGoogleAsksFor()
    {
        var (_, state, start) = Started(Register());

        var uri = new Uri(start.AuthorizationUrl);
        var query = HttpUtility.ParseQueryString(uri.Query);
        Assert.Equal("accounts.google.com", uri.Host);
        Assert.Equal("/o/oauth2/v2/auth", uri.AbsolutePath);
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal(Callback, query["redirect_uri"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("openid email profile", query["scope"]);
        Assert.Equal(state, query["state"]);
        Assert.False(string.IsNullOrWhiteSpace(query["nonce"]));
        Assert.Equal("S256", query["code_challenge_method"]);
        Assert.Equal("select_account", query["prompt"]);
    }

    [Fact]
    public void Begin_NeverPutsTheClientSecretOrTheVerifierInTheAddress()
    {
        var (cookie, _, start) = Started(Register());

        var remembered = _protector.Unprotect(cookie)!;
        Assert.DoesNotContain("the-client-secret", start.AuthorizationUrl);
        Assert.DoesNotContain(remembered.CodeVerifier, start.AuthorizationUrl);
        Assert.DoesNotContain("Northwind", start.AuthorizationUrl);
    }

    [Fact]
    public void Begin_ThePkceChallenge_IsTheSha256OfTheVerifierKeptInTheCookie()
    {
        var (cookie, _, start) = Started(Login());

        var verifier = _protector.Unprotect(cookie)!.CodeVerifier;
        var expected = Base64Url.EncodeToString(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        Assert.Equal(expected, HttpUtility.ParseQueryString(new Uri(start.AuthorizationUrl).Query)["code_challenge"]);
        Assert.InRange(verifier.Length, 43, 128);
    }

    [Fact]
    public void Begin_RemembersWhatThePersonChose_ForTenMinutes()
    {
        var (cookie, state, start) = Started(Register("creator", "  Maya Makes Videos  "));

        var remembered = _protector.Unprotect(cookie)!;
        Assert.Equal(state, remembered.State);
        Assert.Equal("register", remembered.Intent);
        Assert.Equal("creator", remembered.BusinessModel);
        Assert.Equal("Maya Makes Videos", remembered.OrganizationName);
        Assert.Equal(Callback, remembered.RedirectUri);
        Assert.Equal(_time.Now.Add(GoogleSignInService.StateLifetime), start.ExpiresAt);
        Assert.Equal(TimeSpan.FromMinutes(10), GoogleSignInService.StateLifetime);
    }

    [Fact]
    public void Begin_ForALogin_RemembersNoOrganization()
    {
        var (cookie, _, _) = Started(new GoogleStartRequest("login", "company", "Ignored Inc"));

        var remembered = _protector.Unprotect(cookie)!;
        Assert.Equal("login", remembered.Intent);
        Assert.Null(remembered.BusinessModel);
        Assert.Null(remembered.OrganizationName);
    }

    [Fact]
    public void Begin_EverySignIn_GetsItsOwnRandomValues()
    {
        var first = Started(Login());
        var second = Started(Login());

        var a = _protector.Unprotect(first.Cookie)!;
        var b = _protector.Unprotect(second.Cookie)!;
        Assert.NotEqual(a.State, b.State);
        Assert.NotEqual(a.Nonce, b.Nonce);
        Assert.NotEqual(a.CodeVerifier, b.CodeVerifier);
        Assert.NotEqual(a.State, a.Nonce);
    }

    [Fact]
    public void Begin_UsesTheConfiguredRedirectAddress_WhenThereIsOne()
    {
        _settings.RedirectUri = "https://app.example.com/v1/auth/google/callback";

        var (cookie, _, start) = Started(Login());

        Assert.Equal("https://app.example.com/v1/auth/google/callback", _protector.Unprotect(cookie)!.RedirectUri);
        Assert.Equal(
            "https://app.example.com/v1/auth/google/callback",
            HttpUtility.ParseQueryString(new Uri(start.AuthorizationUrl).Query)["redirect_uri"]);
    }

    [Fact]
    public void Begin_WithGoogleOff_IsNotFound()
    {
        _settings.ClientId = "";
        _settings.ClientSecret = "";

        var exception = Assert.Throws<NotFoundException>(() => _service.Begin(Login(), Callback));

        Assert.Equal("google_unavailable", exception.Code);
        Assert.False(_service.Enabled);
    }

    [Theory]
    [InlineData(null, null, null)]
    [InlineData("signup", null, null)]
    [InlineData("register", null, "Acme")]
    [InlineData("register", "individual", "Acme")]
    [InlineData("register", "company", "")]
    [InlineData("register", "company", null)]
    public void Begin_WithAnIncompleteOrUnknownRequest_IsABadRequest(string? intent, string? model, string? name)
    {
        Assert.Throws<BadRequestException>(() => _service.Begin(new GoogleStartRequest(intent, model, name), Callback));
    }

    // --- Complete: refusing a callback this browser did not start ----------------------------

    [Fact]
    public async Task Complete_WithoutTheStateCookie_IsRefused_BeforeGoogleIsAsked()
    {
        var (_, state, _) = Started(Login());

        var outcome = await CompleteAsync(cookie: null, state);

        Assert.Equal(GoogleSignInErrors.SessionExpired, outcome.ErrorCode);
        await _google.DidNotReceiveWithAnyArgs().ExchangeCodeAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Complete_WhenTheStateInTheAddressIsNotTheOneInTheCookie_IsRefused()
    {
        var (cookie, _, _) = Started(Login());

        var outcome = await CompleteAsync(cookie, state: "a-state-from-somebody-elses-sign-in");

        Assert.Equal(GoogleSignInErrors.SessionExpired, outcome.ErrorCode);
        await _google.DidNotReceiveWithAnyArgs().ExchangeCodeAsync(default!, default!, default!, default!, default);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task Complete_WithoutAStateInTheAddress_IsRefused(string? state)
    {
        var (cookie, _, _) = Started(Login());

        Assert.Equal(GoogleSignInErrors.SessionExpired, (await CompleteAsync(cookie, state)).ErrorCode);
    }

    [Fact]
    public async Task Complete_AfterTheStateExpired_IsRefused()
    {
        var (cookie, state, _) = Started(Login());
        _time.Advance(GoogleSignInService.StateLifetime + TimeSpan.FromSeconds(1));

        Assert.Equal(GoogleSignInErrors.SessionExpired, (await CompleteAsync(cookie, state)).ErrorCode);
    }

    [Fact]
    public async Task Complete_AfterARefusedAttempt_ReturnsThePersonToTheFormTheyStartedFrom()
    {
        var (cookie, _, _) = Started(Register());

        var outcome = await CompleteAsync(cookie, state: "wrong");

        Assert.Equal("register", outcome.Intent);
    }

    [Fact]
    public async Task Complete_WithNoUsableCookie_FallsBackToTheLoginForm()
    {
        Assert.Equal("login", (await CompleteAsync(cookie: "junk", state: "x")).Intent);
    }

    // --- Complete: what Google says ----------------------------------------------------------

    [Fact]
    public async Task Complete_WhenThePersonRefusedAtGoogle_SaysSo_WithoutAskingForTokens()
    {
        var (cookie, state, _) = Started(Login());

        var outcome = await CompleteAsync(cookie, state, code: null, error: "access_denied");

        Assert.Equal(GoogleSignInErrors.Denied, outcome.ErrorCode);
        await _google.DidNotReceiveWithAnyArgs().ExchangeCodeAsync(default!, default!, default!, default!, default);
    }

    [Fact]
    public async Task Complete_WithoutACode_Fails()
    {
        var (cookie, state, _) = Started(Login());

        Assert.Equal(GoogleSignInErrors.Failed, (await CompleteAsync(cookie, state, code: null)).ErrorCode);
    }

    [Fact]
    public async Task Complete_SendsGoogleTheVerifierTheNonceAndTheRedirectAddressFromTheCookie()
    {
        var (cookie, state, _) = Started(Login());
        var remembered = _protector.Unprotect(cookie)!;

        await CompleteAsync(cookie, state, code: "the-one-time-code");

        await _google.Received(1).ExchangeCodeAsync(
            "the-one-time-code", remembered.CodeVerifier, Callback, remembered.Nonce, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Complete_WhenGoogleRefusesTheCodeOrTheTokenIsBad_Fails_AndNobodyIsSignedIn()
    {
        _google.ExchangeCodeAsync(default!, default!, default!, default!, default)
            .ThrowsAsyncForAnyArgs(new GoogleSignInException("The Google ID token is not valid."));
        var (cookie, state, _) = Started(Register());

        var outcome = await CompleteAsync(cookie, state);

        Assert.Equal(GoogleSignInErrors.Failed, outcome.ErrorCode);
        Assert.Null(outcome.Session);
        await _auth.DidNotReceiveWithAnyArgs().RegisterExternalAsync(default!, default);
        await _auth.DidNotReceiveWithAnyArgs().LoginExternalAsync(default, default);
    }

    [Fact]
    public async Task Complete_WithAnEmailGoogleHasNotVerified_Fails_AndLooksNoFurther()
    {
        _google.ExchangeCodeAsync(default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(Maya with { EmailVerified = false });
        var (cookie, state, _) = Started(Register());

        var outcome = await CompleteAsync(cookie, state);

        Assert.Equal(GoogleSignInErrors.EmailUnverified, outcome.ErrorCode);
        await _accounts.DidNotReceiveWithAnyArgs().FindUserIdByIdentityAsync(default!, default!, default);
        await _auth.DidNotReceiveWithAnyArgs().RegisterExternalAsync(default!, default);
    }

    // --- Complete: a Google user we already know ---------------------------------------------

    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    public async Task Complete_ForAGoogleUserWeKnow_SignsThemIn_WhicheverFormTheyStartedFrom(string intent)
    {
        var userId = Guid.NewGuid();
        _accounts.FindUserIdByIdentityAsync("google", "sub-maya", Arg.Any<CancellationToken>()).Returns(userId);
        var (cookie, state, _) = Started(intent == "login" ? Login() : Register());

        var outcome = await CompleteAsync(cookie, state);

        Assert.Same(Session, outcome.Session);
        Assert.Null(outcome.ErrorCode);
        await _auth.Received(1).LoginExternalAsync(userId, Arg.Any<CancellationToken>());
        await _auth.DidNotReceiveWithAnyArgs().RegisterExternalAsync(default!, default);
    }

    [Fact]
    public async Task Complete_ForAKnownGoogleUserWithNoOrganization_SaysSo()
    {
        _accounts.FindUserIdByIdentityAsync("google", "sub-maya", Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        _auth.LoginExternalAsync(default, default).ThrowsAsyncForAnyArgs(new ForbiddenException("none", "no_agency"));
        var (cookie, state, _) = Started(Login());

        Assert.Equal(GoogleSignInErrors.NoOrganization, (await CompleteAsync(cookie, state)).ErrorCode);
    }

    [Theory]
    [InlineData("account_deactivated", GoogleSignInErrors.AccountDeactivated)]
    [InlineData("organization_deactivated", GoogleSignInErrors.OrganizationDeactivated)]
    [InlineData("no_agency", GoogleSignInErrors.NoOrganization)]
    [InlineData("anything_else", GoogleSignInErrors.NoOrganization)]
    public async Task Complete_ForAKnownGoogleUserWhoCannotSignIn_SaysWhyInItsOwnWords(string refusal, string expected)
    {
        _accounts.FindUserIdByIdentityAsync("google", "sub-maya", Arg.Any<CancellationToken>()).Returns(Guid.NewGuid());
        _auth.LoginExternalAsync(default, default).ThrowsAsyncForAnyArgs(new ForbiddenException("refused", refusal));
        var (cookie, state, _) = Started(Login());

        var outcome = await CompleteAsync(cookie, state);

        Assert.Equal(expected, outcome.ErrorCode);
        Assert.Null(outcome.Session);
    }

    [Fact]
    public void TheDeactivatedErrorCodes_AreTheOnesTheWebClientHasWordsFor()
    {
        Assert.Equal("google_account_deactivated", GoogleSignInErrors.AccountDeactivated);
        Assert.Equal("google_organization_deactivated", GoogleSignInErrors.OrganizationDeactivated);
    }

    // --- Complete: an address that already has an account ------------------------------------

    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    public async Task Complete_WhenTheEmailAlreadyHasAnAccount_IsRefused_AndNeverLinked(string intent)
    {
        _accounts.FindLoginByEmailAsync("maya.rao@gmail.com", Arg.Any<CancellationToken>())
            .Returns(new UserLoginRecord { Id = Guid.NewGuid(), Email = "maya.rao@gmail.com", PasswordHash = "pbkdf2$1$c2FsdA==$AAAAAAAAAAAAAAAAAAAAAA==" });
        var (cookie, state, _) = Started(intent == "login" ? Login() : Register());

        var outcome = await CompleteAsync(cookie, state);

        Assert.Equal(GoogleSignInErrors.EmailTaken, outcome.ErrorCode);
        Assert.Null(outcome.Session);
        await _auth.DidNotReceiveWithAnyArgs().RegisterExternalAsync(default!, default);
        await _auth.DidNotReceiveWithAnyArgs().LoginExternalAsync(default, default);
    }

    [Fact]
    public async Task Complete_TheEmailIsLookedUpInLowerCase_SoCapitalsInGoogleAddressesDoNotHideAnAccount()
    {
        var (cookie, state, _) = Started(Register());

        await CompleteAsync(cookie, state);

        await _accounts.Received(1).FindLoginByEmailAsync("maya.rao@gmail.com", Arg.Any<CancellationToken>());
    }

    // --- Complete: signing in without an account ---------------------------------------------

    [Fact]
    public async Task Complete_ForALoginWithNoAccount_SendsThemToRegisterRatherThanMakingOne()
    {
        var (cookie, state, _) = Started(Login());

        var outcome = await CompleteAsync(cookie, state);

        Assert.Equal(GoogleSignInErrors.NoAccount, outcome.ErrorCode);
        Assert.Equal("login", outcome.Intent);
        await _auth.DidNotReceiveWithAnyArgs().RegisterExternalAsync(default!, default);
    }

    // --- Complete: registering ---------------------------------------------------------------

    [Theory]
    [InlineData("company", BusinessModel.Company)]
    [InlineData("agency", BusinessModel.Agency)]
    [InlineData("creator", BusinessModel.Creator)]
    public async Task Complete_ForANewPerson_CreatesTheOrganizationTheyChose_FromGoogleDetails(string text, BusinessModel model)
    {
        ExternalRegistration? sent = null;
        _auth.RegisterExternalAsync(Arg.Do<ExternalRegistration>(r => sent = r), Arg.Any<CancellationToken>()).Returns(Session);
        var (cookie, state, _) = Started(Register(text, "Brightside"));

        var outcome = await CompleteAsync(cookie, state);

        Assert.Same(Session, outcome.Session);
        Assert.Null(outcome.ErrorCode);
        Assert.Equal(
            new ExternalRegistration("google", "sub-maya", "maya.rao@gmail.com", "Maya Rao", "Brightside", model), sent);
    }

    [Fact]
    public async Task Complete_AGoogleNameLongerThanTheDatabaseKeeps_IsShortenedNotRefused()
    {
        _google.ExchangeCodeAsync(default!, default!, default!, default!, default)
            .ReturnsForAnyArgs(Maya with { FullName = new string('n', 500) });
        ExternalRegistration? sent = null;
        _auth.RegisterExternalAsync(Arg.Do<ExternalRegistration>(r => sent = r), Arg.Any<CancellationToken>()).Returns(Session);
        var (cookie, state, _) = Started(Register());

        await CompleteAsync(cookie, state);

        Assert.Equal(200, sent!.FullName!.Length);
    }

    [Fact]
    public async Task Complete_WhenSomeoneTookTheAddressMeanwhile_ReportsItAsTaken()
    {
        _auth.RegisterExternalAsync(default!, default).ThrowsAsyncForAnyArgs(new ConflictException("taken", "email_taken"));
        var (cookie, state, _) = Started(Register());

        var outcome = await CompleteAsync(cookie, state);

        Assert.Equal(GoogleSignInErrors.EmailTaken, outcome.ErrorCode);
        Assert.Equal("register", outcome.Intent);
    }

    [Fact]
    public async Task Complete_WhenTheRememberedChoiceIsNoLongerValid_FailsAsABadRequest()
    {
        // The state cookie is ours and signed, so this needs a model name written by an older or newer build.
        var forged = _protector.Protect(new GoogleOAuthState(
            "s", "n", "v", "register", "franchise", "Acme", Callback, _time.Now.AddMinutes(5)));

        var outcome = await CompleteAsync(forged, "s");

        Assert.Equal(GoogleSignInErrors.InvalidRequest, outcome.ErrorCode);
        await _auth.DidNotReceiveWithAnyArgs().RegisterExternalAsync(default!, default);
    }

    [Fact]
    public async Task Complete_TheSameCallbackCannotBeUsedTwiceToRegisterTwice_BecauseTheSecondFindsTheFirstsIdentity()
    {
        var (cookie, state, _) = Started(Register());
        await CompleteAsync(cookie, state);
        var userId = Guid.NewGuid();
        _accounts.FindUserIdByIdentityAsync("google", "sub-maya", Arg.Any<CancellationToken>()).Returns(userId);

        var again = await CompleteAsync(cookie, state);

        Assert.Same(Session, again.Session);
        await _auth.Received(1).RegisterExternalAsync(Arg.Any<ExternalRegistration>(), Arg.Any<CancellationToken>());
        await _auth.Received(1).LoginExternalAsync(userId, Arg.Any<CancellationToken>());
    }
}
