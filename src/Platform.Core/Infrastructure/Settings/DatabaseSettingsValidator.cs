using Microsoft.Extensions.Options;
using Npgsql;

namespace Platform.Infrastructure.Settings;

public sealed class DatabaseSettingsValidator : IValidateOptions<DatabaseSettings>
{
    public ValidateOptionsResult Validate(string? name, DatabaseSettings options)
    {
        if (string.IsNullOrWhiteSpace(options.AppConnectionString))
        {
            return ValidateOptionsResult.Fail(
                "ConnectionStrings:App is not configured. It belongs in appsettings.json (Host, Port, Database and " +
                "Username=app_user), with its password in user-secrets (Database:AppPassword) or the Database__AppPassword " +
                "environment variable; it must log in as the app_user role.");
        }

        try
        {
            _ = new NpgsqlConnectionStringBuilder(options.AppConnectionString);
            if (!string.IsNullOrWhiteSpace(options.MigratorConnectionString))
            {
                _ = new NpgsqlConnectionStringBuilder(options.MigratorConnectionString);
            }
        }
        catch (ArgumentException)
        {
            return ValidateOptionsResult.Fail("A connection string under ConnectionStrings is not a valid PostgreSQL connection string.");
        }

        return ValidateOptionsResult.Success;
    }
}
