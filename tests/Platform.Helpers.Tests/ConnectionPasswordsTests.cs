using Npgsql;
using Platform.Infrastructure.Settings;

namespace Platform.Helpers.Tests;

public sealed class ConnectionPasswordsTests
{
    private const string WithoutPassword = "Host=localhost;Port=5432;Database=justmakeitcamp;Username=app_user";

    [Fact]
    public void Apply_AddsThePassword_WhenTheConnectionStringHasNone_AndKeepsEverythingElse()
    {
        var joined = new NpgsqlConnectionStringBuilder(ConnectionPasswords.Apply(WithoutPassword, "s3cret"));

        Assert.Equal("s3cret", joined.Password);
        Assert.Equal("localhost", joined.Host);
        Assert.Equal(5432, joined.Port);
        Assert.Equal("justmakeitcamp", joined.Database);
        Assert.Equal("app_user", joined.Username);
    }

    [Theory]
    [InlineData("p;w=d")]
    [InlineData("a b c")]
    [InlineData("quo'te\"s")]
    [InlineData("semi;colon;Host=evil")]
    public void Apply_KeepsAPasswordThatLooksLikeConnectionStringSyntax_AsOnePassword(string password)
    {
        var joined = new NpgsqlConnectionStringBuilder(ConnectionPasswords.Apply(WithoutPassword, password));

        Assert.Equal(password, joined.Password);
        Assert.Equal("localhost", joined.Host); // the password cannot add or change another setting
        Assert.Equal("app_user", joined.Username);
    }

    [Fact]
    public void Apply_LeavesAPasswordWrittenInTheStringAlone_SoAnOlderSecretStillWins()
    {
        var written = WithoutPassword + ";Password=from-the-string";

        Assert.Equal(written, ConnectionPasswords.Apply(written, "from-the-setting"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Apply_ChangesNothing_WhenThereIsNoPasswordToAdd(string? password) =>
        Assert.Equal(WithoutPassword, ConnectionPasswords.Apply(WithoutPassword, password));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Apply_GivesNothing_WhenThereIsNoConnectionString_SoTheValidatorSaysItIsNotConfigured(string? connectionString) =>
        Assert.Equal(string.Empty, ConnectionPasswords.Apply(connectionString, "s3cret"));

    [Fact]
    public void Apply_ReturnsAnInvalidConnectionStringAsItIs_WithoutThrowingOrRepeatingTheSecret()
    {
        const string invalid = "this is not a connection string";

        var result = ConnectionPasswords.Apply(invalid, "s3cret");

        Assert.Equal(invalid, result);
        Assert.DoesNotContain("s3cret", result);
    }

    [Fact]
    public void TheKeys_AreTheOnesTheCommentsAndTheDocumentationName()
    {
        Assert.Equal("Database:AppPassword", ConnectionPasswords.AppPasswordKey);
        Assert.Equal("Database:MigratorPassword", ConnectionPasswords.MigratorPasswordKey);
    }
}
