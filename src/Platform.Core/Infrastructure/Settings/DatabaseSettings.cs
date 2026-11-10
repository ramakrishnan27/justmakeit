namespace Platform.Infrastructure.Settings;

public sealed class DatabaseSettings
{
    public const string AppConnectionName = "App";
    public const string MigratorConnectionName = "Migrator";

    /// <summary>
    /// Connection string for the API at runtime, for every query a request makes. Must log in as <c>app_user</c>.
    /// Written in appsettings.json without a password (see <see cref="ConnectionPasswords"/>).
    /// </summary>
    public string AppConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Connection string for the database owner. Used only by <see cref="Data.DbMigrator"/> and the site admin bootstrap.
    /// Written in appsettings.json without a password (see <see cref="ConnectionPasswords"/>).
    /// </summary>
    public string MigratorConnectionString { get; set; } = string.Empty;

    /// <summary>Apply migrations when the API starts. Development always does, whatever this says.</summary>
    public bool MigrateOnStartup { get; set; }
}
