namespace Platform.Infrastructure.Settings;

public sealed class DatabaseSettings
{
    public const string AppConnectionName = "App";
    public const string MigratorConnectionName = "Migrator";

    /// <summary>Connection string for the API at runtime. Must log in as <c>app_user</c>.</summary>
    public string AppConnectionString { get; set; } = string.Empty;

    /// <summary>Connection string for the database owner. Used only by <see cref="Data.DbMigrator"/>.</summary>
    public string MigratorConnectionString { get; set; } = string.Empty;

    /// <summary>Apply migrations when the API starts. Development always does, whatever this says.</summary>
    public bool MigrateOnStartup { get; set; }
}
