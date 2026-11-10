using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using Platform.Helpers.Tenancy;
using Platform.Infrastructure.Data;
using Platform.Infrastructure.Health;
using Platform.Infrastructure.Settings;

namespace Platform.Infrastructure;

public static class InfrastructureModule
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services)
    {
        DapperConfiguration.Configure();

        // Configuration is read when the options are first used, not while services are registered,
        // so every configuration source (user-secrets, environment, test overrides) has been added by then.
        services.AddOptions<DatabaseSettings>().Configure<IConfiguration>((settings, configuration) =>
        {
            // The strings are in appsettings.json without passwords; each password is set apart and joined here.
            settings.AppConnectionString = ConnectionPasswords.Apply(
                configuration.GetConnectionString(DatabaseSettings.AppConnectionName), configuration[ConnectionPasswords.AppPasswordKey]);
            settings.MigratorConnectionString = ConnectionPasswords.Apply(
                configuration.GetConnectionString(DatabaseSettings.MigratorConnectionName), configuration[ConnectionPasswords.MigratorPasswordKey]);
            settings.MigrateOnStartup = configuration.GetValue<bool>("Database:MigrateOnStartup");
        });
        services.AddSingleton<IValidateOptions<DatabaseSettings>, DatabaseSettingsValidator>();

        services.AddSingleton(provider =>
            NpgsqlDataSource.Create(provider.GetRequiredService<IOptions<DatabaseSettings>>().Value.AppConnectionString));

        services.AddScoped<TenantContext>();
        services.AddScoped<ITenantContext>(provider => provider.GetRequiredService<TenantContext>());
        services.AddScoped<IDbConnectionFactory, NpgsqlConnectionFactory>();
        services.AddSingleton<DbMigrator>();

        services.AddHealthChecks().AddCheck<DatabaseHealthCheck>("database");

        return services;
    }
}
