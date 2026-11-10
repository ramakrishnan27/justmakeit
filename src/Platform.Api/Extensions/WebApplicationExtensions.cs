using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Options;
using Platform.Auth.Interfaces;
using Platform.Auth.Settings;
using Platform.Infrastructure.Data;
using Platform.Infrastructure.Settings;

namespace Platform.Api.Extensions;

public static class WebApplicationExtensions
{
    /// <summary>
    /// The addresses of the member app (sign in, create an account, dashboard, workspaces). The site admin app lives
    /// under /admin. A page that a menu item links to needs its address here, or reloading it would answer 404; a test
    /// checks that every address in <c>menu_pages</c> serves the app shell.
    /// </summary>
    private static readonly string[] MemberAppPages =
    [
        "login", "register", "dashboard", "content-studio", "media-library", "ai-studio", "calendar",
        "workspaces", "platforms", "team", "accept-invite", "account"
    ];

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
        _ = app.Services.GetRequiredService<IOptions<SiteAdminSettings>>().Value;
        _ = app.Services.GetRequiredService<IOptions<GoogleSettings>>().Value;
        var database = app.Services.GetRequiredService<IOptions<DatabaseSettings>>().Value;

        if (app.Environment.IsDevelopment() || database.MigrateOnStartup)
        {
            await app.Services.GetRequiredService<DbMigrator>().MigrateAsync(app.Lifetime.ApplicationStopping);

            // Only where the owner connection is already trusted to migrate: the first site admin is
            // written by the owner, never by the application role.
            await app.Services.GetRequiredService<ISiteAdminBootstrapper>().EnsureAsync(app.Lifetime.ApplicationStopping);
        }
    }

    /// <summary>
    /// Lets a browser open or reload a client-side route such as /admin/login or /login: those paths answer
    /// with the app shell (<c>index.html</c>) instead of 404. Only the app addresses are covered (/admin and
    /// the member app: /login, /register, /dashboard, /workspaces), so a mistyped /v1 route still gets a real 404 from the API.
    /// </summary>
    public static IEndpointRouteBuilder MapPlatformWebFallback(this WebApplication app)
    {
        var root = ResolveWebRoot(app);
        if (root is null)
        {
            return app;
        }

        var options = new StaticFileOptions { FileProvider = new PhysicalFileProvider(root) };

        // The authorization fallback policy would otherwise close these to a visitor who is not signed in yet.
        app.MapFallbackToFile("admin", "index.html", options).AllowAnonymous();
        app.MapFallbackToFile("admin/{*path}", "index.html", options).AllowAnonymous();
        foreach (var page in MemberAppPages)
        {
            app.MapFallbackToFile(page, "index.html", options).AllowAnonymous();
        }

        // Content Studio has pages under its own address: /content-studio/new and /content-studio/{id} (the editor), which are not
        // menu items. Only this one address has a deeper part: /dashboard/anything and the others stay a real 404.
        app.MapFallbackToFile("content-studio/{*path}", "index.html", options).AllowAnonymous();

        return app;
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

        // web/tests holds the frontend unit tests and their doubles: part of the repository, never of the site.
        app.Use(async (context, next) =>
        {
            if (context.Request.Path.StartsWithSegments("/tests", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            await next(context);
        });

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
