using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Platform.Auth.Settings;
using Platform.Infrastructure.Data;
using Platform.Infrastructure.Settings;

namespace Platform.Api.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// Runs before the first request: stops the app on invalid configuration, then applies migrations
    /// in Development (or wherever <c>Database:MigrateOnStartup</c> is true).
    /// </summary>
    public static async Task RunStartupTasksAsync(this WebApplication app)
    {
        // Reading .Value runs the options validators, so a missing or short Jwt:SigningKey, or a missing
        // connection string, stops the app here with a clear message, before the database is touched.
        _ = app.Services.GetRequiredService<IOptions<JwtSettings>>().Value;
        _ = app.Services.GetRequiredService<IOptions<AuthRateLimitSettings>>().Value;
        var database = app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;

        if (app.Environment.IsDevelopment() || database.MigrateOnStartup)
        {
            await app.Services.GetRequiredService<DbMigrator>().MigrateAsync(app.Lifetime.ApplicationStopping);
        }
    }

    /// <summary>Allows the configured web origin (<c>Cors:WebOrigin</c>). Without one, no cross-origin access is granted.</summary>
    public static IApplicationBuilder UsePlatformCors(this WebApplication app)
    {
        var origin = app.Configuration["Cors:WebOrigin"];
        return string.IsNullOrWhiteSpace(origin)
            ? app
            : app.UseCors(policy => policy.WithOrigins(origin).AllowAnyHeader().AllowAnyMethod().AllowCredentials());
    }

    /// <summary>Serves the web/ folder: <c>Web:RootPath</c> if set, else ./web next to the app, else ../../web (the repository layout).</summary>
    public static IApplicationBuilder UsePlatformStaticFiles(this WebApplication app)
    {
        var root = ResolveWebRoot(app);
        if (root is null)
        {
            app.Logger.LogWarning("The web folder was not found, so the frontend is not served.");
            return app;
        }

        var files = new PhysicalFileProvider(root);
        app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = files });
        app.UseStaticFiles(new StaticFileOptions { FileProvider = files });
        return app;
    }

    private static string? ResolveWebRoot(WebApplication app)
    {
        var configured = app.Configuration["Web:RootPath"];
        string[] candidates =
        [
            configured ?? string.Empty,
            Path.Combine(app.Environment.ContentRootPath, "web"),
            Path.Combine(app.Environment.ContentRootPath, "..", "..", "web")
        ];

        return candidates
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .FirstOrDefault(Directory.Exists);
    }
}
