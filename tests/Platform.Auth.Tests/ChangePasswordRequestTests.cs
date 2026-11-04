using Platform.Auth.DTOs;

namespace Platform.Auth.Tests;

public sealed class ChangePasswordRequestTests
{
    private static string[] Fields(Platform.Helpers.Validation.ValidationErrors errors) => [.. errors.ToDictionary().Keys.Order()];

    [Theory]
    [InlineData("anything at all", "0123456789")]
    [InlineData("x", "a much longer password than ten")]
    public void ACurrentPasswordAndANewOneOf10To128Characters_IsValid(string current, string next) =>
        Assert.False(new ChangePasswordRequest(current, next).Validate().Any);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void ACurrentPasswordThatIsMissing_ReportsTheCurrentPasswordField(string? current) =>
        Assert.Equal(["currentPassword"], Fields(new ChangePasswordRequest(current, "a brand new password").Validate()));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    public void ANewPasswordThatIsMissingOrShort_ReportsTheNewPasswordField(string? next) =>
        Assert.Equal(["newPassword"], Fields(new ChangePasswordRequest("current one", next).Validate()));

    [Fact]
    public void ANewPasswordOver128Characters_ReportsTheNewPasswordField() =>
        Assert.Equal(["newPassword"], Fields(new ChangePasswordRequest("current one", new string('a', 129)).Validate()));

    [Fact]
    public void ACurrentPasswordOver128Characters_ReportsTheCurrentPasswordField_SoNothingHugeIsEverHashed() =>
        Assert.Equal(["currentPassword"], Fields(new ChangePasswordRequest(new string('a', 129), "a brand new password").Validate()));

    [Fact]
    public void NothingAtAll_ReportsBothFields() =>
        Assert.Equal(["currentPassword", "newPassword"], Fields(new ChangePasswordRequest(null, null).Validate()));
}
