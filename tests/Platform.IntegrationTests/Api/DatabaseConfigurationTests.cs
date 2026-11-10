using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Infrastructure.Settings;

namespace Platform.IntegrationTests.Api;

/// <summary>
/// Where the API gets its database connections: the two connection strings are in the committed appsettings.json without
/// a password, each password is set apart, and the settings the API reads are the two joined. No database is needed.
/// </summary>
public sealed class DatabaseConfigurationTests
{
    private const string Unreachable = "Host=127.0.0.1;Port=1;Database=none;Timeout=3";

    private static JsonElement CommittedSettings()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var file = Path.Combine(directory.FullName, "src", "Platform.Api", "appsettings.json");
            if (File.Exists(file))
            {
                return JsonDocument.Parse(File.ReadAllText(file)).RootElement.Clone();
            }
        }

        throw new FileNotFoundException("src/Platform.Api/appsettings.json was not found above " + AppContext.BaseDirectory);
    }

    private static DatabaseSettings SettingsOf(ApiTestHost host) =>
        host.Factory.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;

    [Fact]
    public void TheCommittedFile_HoldsBothConnectionStrings_ForTheTwoLogins_AndNoPasswordInEither()
    {
        var strings = CommittedSettings().GetProperty("ConnectionStrings");

        var app = new NpgsqlConnectionStringBuilder(strings.GetProperty("App").GetString());
        var migrator = new NpgsqlConnectionStringBuilder(strings.GetProperty("Migrator").GetString());

        Assert.Equal("app_user", app.Username);
        Assert.NotEqual("app_user", migrator.Username); // the database owner, never the application login
        Assert.Equal(app.Host, migrator.Host);
        Assert.Equal(app.Port, migrator.Port);
        Assert.Equal(app.Database, migrator.Database); // one database, two logins
        Assert.True(string.IsNullOrEmpty(app.Password), "appsettings.json is committed: no password in the App string.");
        Assert.True(string.IsNullOrEmpty(migrator.Password), "appsettings.json is committed: no password in the Migrator string.");
    }

    [Fact]
    public void TheCommittedFile_LeavesBothPasswordSlotsEmpty_SoNoSecretIsEverCommitted()
    {
        var database = CommittedSettings().GetProperty("Database");

        Assert.Equal(string.Empty, database.GetProperty("AppPassword").GetString());
        Assert.Equal(string.Empty, database.GetProperty("MigratorPassword").GetString());
        Assert.False(database.GetProperty("MigrateOnStartup").GetBoolean(), "Production does not migrate unless asked; Development always does.");
    }

    [Fact]
    public async Task ATestHost_NeverInheritsTheCommittedConnections_SoATestCannotReachTheDevelopmentDatabaseByAccident()
    {
        await using var host = ApiTestHost.StartWithoutDatabase();

        var settings = SettingsOf(host);

        Assert.Equal(ApiHostFactory.UnreachableConnectionString, settings.AppConnectionString);
        Assert.Equal(string.Empty, settings.MigratorConnectionString);
    }

    [Fact]
    public async Task EachPassword_IsJoinedToItsOwnConnectionString_AndOnlyToThat()
    {
        await using var host = ApiTestHost.StartWithoutDatabase(configure: settings =>
        {
            settings["ConnectionStrings:App"] = Unreachable + ";Username=app_user";
            settings["ConnectionStrings:Migrator"] = Unreachable + ";Username=the_owner";
            settings[ConnectionPasswords.AppPasswordKey] = "app-secret";
            settings[ConnectionPasswords.MigratorPasswordKey] = "owner-secret";
        });

        var settings = SettingsOf(host);

        var app = new NpgsqlConnectionStringBuilder(settings.AppConnectionString);
        var migrator = new NpgsqlConnectionStringBuilder(settings.MigratorConnectionString);
        Assert.Equal(("app_user", "app-secret"), (app.Username, app.Password));
        Assert.Equal(("the_owner", "owner-secret"), (migrator.Username, migrator.Password));
    }

    [Fact]
    public async Task APasswordWrittenInAConnectionString_StillWins_SoOlderSecretsKeepWorking()
    {
        await using var host = ApiTestHost.StartWithoutDatabase(configure: settings =>
        {
            settings["ConnectionStrings:App"] = Unreachable + ";Username=app_user;Password=old-secret";
            settings[ConnectionPasswords.AppPasswordKey] = "new-secret";
        });

        Assert.Equal("old-secret", new NpgsqlConnectionStringBuilder(SettingsOf(host).AppConnectionString).Password);
    }

    [Fact]
    public async Task APasswordSetting_WithNoConnectionString_DoesNotMakeOneUp()
    {
        await using var host = ApiTestHost.StartWithoutDatabase(configure: settings =>
        {
            settings["ConnectionStrings:Migrator"] = null;
            settings[ConnectionPasswords.MigratorPasswordKey] = "owner-secret";
        });

        Assert.Equal(string.Empty, SettingsOf(host).MigratorConnectionString);
    }
}
