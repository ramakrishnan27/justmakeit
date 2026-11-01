using Microsoft.AspNetCore.DataProtection;
using Platform.Auth.Services.Google;
using Platform.Auth.Tests.Support;

namespace Platform.Auth.Tests;

public sealed class GoogleOAuthStateProtectorTests
{
    private readonly FixedTimeProvider _time = FixedTimeProvider.Create();
    private readonly IDataProtectionProvider _keys = new EphemeralDataProtectionProvider();

    private GoogleOAuthStateProtector Create(IDataProtectionProvider? keys = null) => new(keys ?? _keys, _time);

    private GoogleOAuthState Sample(TimeSpan? lifetime = null) => new(
        State: "state-value",
        Nonce: "nonce-value",
        CodeVerifier: "verifier-value",
        Intent: "register",
        BusinessModel: "creator",
        OrganizationName: "Maya Makes Videos",
        RedirectUri: "http://localhost:5080/v1/auth/google/callback",
        ExpiresAt: _time.Now.Add(lifetime ?? TimeSpan.FromMinutes(10)));

    [Fact]
    public void WhatWasProtected_ComesBackUnchanged()
    {
        var protector = Create();

        var back = protector.Unprotect(protector.Protect(Sample()));

        Assert.Equal(Sample(), back);
    }

    [Fact]
    public void TheProtectedValue_DoesNotShowWhatIsInsideIt()
    {
        var value = Create().Protect(Sample());

        Assert.DoesNotContain("verifier-value", value);
        Assert.DoesNotContain("nonce-value", value);
        Assert.DoesNotContain("Maya", value);
    }

    [Fact]
    public void AValueChangedByEvenOneCharacter_IsRefused()
    {
        var protector = Create();
        var value = protector.Protect(Sample());
        var middle = value.Length / 2;
        var tampered = value[..middle] + (value[middle] == 'A' ? 'B' : 'A') + value[(middle + 1)..];

        Assert.Null(protector.Unprotect(tampered));
    }

    [Fact]
    public void AValueMadeWithAnotherKeyRing_IsRefused()
    {
        var value = Create(new EphemeralDataProtectionProvider()).Protect(Sample());

        Assert.Null(Create(new EphemeralDataProtectionProvider()).Unprotect(value));
    }

    [Fact]
    public void AValueFromAnotherPurposeOfTheSameKeyRing_IsRefused()
    {
        // Another part of the app protecting something with the same keys must not be able to forge this state.
        var other = _keys.CreateProtector("Some.Other.Purpose").Protect("""{"State":"s"}""");

        Assert.Null(Create().Unprotect(other));
    }

    [Fact]
    public void AnExpiredState_IsRefused()
    {
        var protector = Create();
        var value = protector.Protect(Sample(TimeSpan.FromMinutes(10)));

        _time.Advance(TimeSpan.FromMinutes(10) + TimeSpan.FromSeconds(1));

        Assert.Null(protector.Unprotect(value));
    }

    [Fact]
    public void AStateJustBeforeItExpires_IsStillGood()
    {
        var protector = Create();
        var value = protector.Protect(Sample(TimeSpan.FromMinutes(10)));

        _time.Advance(TimeSpan.FromMinutes(9));

        Assert.NotNull(protector.Unprotect(value));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not protected at all")]
    [InlineData("CfDJ8-looks-like-one-but-is-not")]
    public void NothingOrJunk_IsRefused_WithoutAnError(string? value)
    {
        Assert.Null(Create().Unprotect(value));
    }

    [Fact]
    public void AnAbsurdlyLongValue_IsRefusedBeforeAnyWorkIsDone()
    {
        Assert.Null(Create().Unprotect(new string('A', 100_000)));
    }
}
