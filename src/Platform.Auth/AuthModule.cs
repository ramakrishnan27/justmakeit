using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Platform.Auth.Interfaces;
using Platform.Auth.Repositories;
using Platform.Auth.Services;
using Platform.Auth.Services.Google;
using Platform.Auth.Settings;

namespace Platform.Auth;

public static class AuthModule
{
    public static IServiceCollection AddAuthModule(this IServiceCollection services)
    {
        services.AddOptions<JwtSettings>().BindConfiguration(JwtSettings.SectionName);
        services.AddSingleton<IValidateOptions<JwtSettings>, JwtSettingsValidator>();
        services.AddSingleton<IConfigureOptions<JwtBearerOptions>, ConfigureJwtBearerOptions>();

        services.AddOptions<AuthRateLimitSettings>().BindConfiguration(AuthRateLimitSettings.SectionName);
        services.AddSingleton<IValidateOptions<AuthRateLimitSettings>, AuthRateLimitSettingsValidator>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddAuthorization(AuthPolicies.Configure);
        services.AddRateLimiter(AuthRateLimitPolicies.AddTo);

        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IPasswordHasher>(_ => new Pbkdf2PasswordHasher());
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();

        // Authorization reads the permissions in the access token; the matrix and its version are held
        // in memory here and read from the database at most once per PermissionService.CacheLifetime.
        services.AddSingleton<IAuthorizationHandler, PermissionAuthorizationHandler>();
        services.AddSingleton<IPermissionService, PermissionService>();
        services.AddScoped<IPermissionRepository, PermissionRepository>();
        services.AddScoped<IPermissionAdminService, PermissionAdminService>();

        // The menus: held in memory like the permissions, so showing one reads no database.
        services.AddSingleton<IMenuCache, MenuCache>();
        services.AddScoped<IMenuRepository, MenuRepository>();
        services.AddScoped<IMenuService, MenuService>();
        services.AddScoped<IMenuAdminService, MenuAdminService>();

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAgencyRepository, AgencyRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();

        // The team: inviting, changing and removing people. Ending a session is held in memory like the permissions,
        // so refusing a removed member's token reads no database per request.
        services.AddOptions<InvitationSettings>().BindConfiguration(InvitationSettings.SectionName);
        services.AddSingleton<IValidateOptions<InvitationSettings>, InvitationSettingsValidator>();
        services.AddScoped<ITeamRepository, TeamRepository>();
        services.AddScoped<IInvitationRepository, InvitationRepository>();
        services.AddScoped<ITeamService, TeamService>();
        services.AddSingleton<ISessionRevocationService, SessionRevocationService>();
        services.AddScoped<ISessionRevocationRepository, SessionRevocationRepository>();

        services.AddOptions<SiteAdminSettings>().BindConfiguration(SiteAdminSettings.SectionName);
        services.AddSingleton<IValidateOptions<SiteAdminSettings>, SiteAdminSettingsValidator>();
        services.AddScoped<ISiteAdminRepository, SiteAdminRepository>();
        services.AddScoped<ISiteAdminAuthService, SiteAdminAuthService>();
        services.AddScoped<ISiteAdminDashboardService, SiteAdminDashboardService>();
        services.AddSingleton<ISiteAdminBootstrapper, SiteAdminBootstrapper>();

        // Continue with Google: off until Google:ClientId and Google:ClientSecret are set.
        services.AddOptions<GoogleSettings>().BindConfiguration(GoogleSettings.SectionName);
        services.AddSingleton<IValidateOptions<GoogleSettings>, GoogleSettingsValidator>();
        services.AddDataProtection();
        services.AddSingleton<IGoogleOAuthStateProtector, GoogleOAuthStateProtector>();
        services.AddSingleton<IGoogleSigningKeys, GoogleSigningKeys>();
        services.AddSingleton<GoogleIdTokenValidator>();
        services.AddHttpClient<IGoogleOAuthClient, GoogleOAuthClient>(client => client.Timeout = TimeSpan.FromSeconds(10));
        services.AddScoped<IGoogleSignInService, GoogleSignInService>();

        // Makes this assembly's controllers part of the app without relying on assembly scanning.
        services.AddControllers().AddApplicationPart(typeof(AuthModule).Assembly);

        return services;
    }
}
