using Npgsql;

namespace Platform.Infrastructure.Settings;

/// <summary>
/// The connection strings live in appsettings.json, which is committed, so they carry no password. The password of each
/// login is set apart (<c>Database:AppPassword</c>, <c>Database:MigratorPassword</c>: user-secrets or an environment
/// variable) and joined to its connection string here. A password written in the connection string itself always wins,
/// so a connection string that still has one (user-secrets, an environment variable, a test) works exactly as before.
/// </summary>
public static class ConnectionPasswords
{
    public const string AppPasswordKey = "Database:AppPassword";
    public const string MigratorPasswordKey = "Database:MigratorPassword";

    /// <returns>
    /// The connection string with <paramref name="password"/> added when the string has none. Unchanged when there is
    /// no password to add, when the string has one, or when it is not a connection string at all (the settings
    /// validator reports that, with a message that names no secret).
    /// </returns>
    public static string Apply(string? connectionString, string? password)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(password))
        {
            return connectionString;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException)
        {
            return connectionString;
        }

        if (!string.IsNullOrEmpty(builder.Password))
        {
            return connectionString;
        }

        builder.Password = password;
        return builder.ConnectionString;
    }
}
