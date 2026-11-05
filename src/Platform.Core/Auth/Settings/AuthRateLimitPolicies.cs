using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Platform.Auth.Settings;

/// <summary>
/// The named rate-limit policies behind <c>[EnableRateLimiting]</c> on the /v1/auth/* endpoints.
/// Each policy counts requests per client address in its own fixed window, and reads its limits
/// from <see cref="AuthRateLimitSettings"/> when the first request from an address arrives.
/// </summary>
public static class AuthRateLimitPolicies
{
    public const string Credentials = "auth-credentials";
    public const string Session = "auth-session";

    public static void AddTo(RateLimiterOptions options)
    {
        options.AddPolicy(Credentials, context => Partition(context, Credentials, s => s.CredentialRequestsPerWindow));
        options.AddPolicy(Session, context => Partition(context, Session, s => s.SessionRequestsPerWindow));
    }

    private static RateLimitPartition<string> Partition(
        HttpContext context, string policy, Func<AuthRateLimitSettings, int> permitLimit)
    {
        var settings = context.RequestServices.GetRequiredService<IOptions<AuthRateLimitSettings>>().Value;
        var client = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter($"{policy}|{client}", _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = permitLimit(settings),
            Window = TimeSpan.FromSeconds(settings.WindowSeconds),
            QueueLimit = 0
        });
    }
}
