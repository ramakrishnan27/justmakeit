using Microsoft.AspNetCore.Http;

namespace Platform.Auth.Controllers;

/// <summary>
/// The refresh token's cookie: HttpOnly so scripts cannot read it, Secure and SameSite=Strict so it
/// only travels over HTTPS and never on cross-site requests, and scoped to /v1/auth so no other
/// endpoint ever receives it.
/// </summary>
internal static class RefreshTokenCookie
{
    public const string Name = "refresh_token";
    public const string Path = "/v1/auth";

    public static string? Read(HttpRequest request) => request.Cookies[Name];

    public static void Write(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, Options(expires));

    /// <summary>Expires the cookie. It must be sent with the same path as when it was set, or the browser keeps it.</summary>
    public static void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private static CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        IsEssential = true,
        Expires = expires
    };
}
