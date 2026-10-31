using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Platform.Auth.Interfaces;
using Platform.Auth.Repositories;
using Platform.Auth.Services;
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

        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<IAgencyRepository, AgencyRepository>();
        services.AddScoped<IMembershipRepository, MembershipRepository>();
        services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
        services.AddScoped<IWorkspaceRepository, WorkspaceRepository>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IWorkspaceService, WorkspaceService>();

        // Makes this assembly's controllers part of the app without relying on assembly scanning.
        services.AddControllers().AddApplicationPart(typeof(AuthModule).Assembly);

        return services;
    }
}
