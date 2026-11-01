using Microsoft.AspNetCore.Http;

namespace Platform.Auth.Controllers;

/// <summary>
/// A refresh token's cookie: HttpOnly so scripts cannot read it, Secure and SameSite=Strict so it
/// only travels over HTTPS and never on cross-site requests, and scoped to one path so no other
/// endpoint ever receives it. Agency sessions and site admin sessions each get their own name and path.
/// </summary>
internal sealed class RefreshTokenCookie(string name, string path)
{
    /// <summary>Agency members: sent only to /v1/auth.</summary>
    public static readonly RefreshTokenCookie Agency = new("refresh_token", "/v1/auth");

    /// <summary>Site admins: sent only to /v1/site, so the two kinds of session never meet.</summary>
    public static readonly RefreshTokenCookie Site = new("site_refresh_token", "/v1/site");

    public string Name { get; } = name;
    public string Path { get; } = path;

    public string? Read(HttpRequest request) => request.Cookies[Name];

    public void Write(HttpResponse response, string token, DateTimeOffset expires) =>
        response.Cookies.Append(Name, token, Options(expires));

    /// <summary>Expires the cookie. It must be sent with the same path as when it was set, or the browser keeps it.</summary>
    public void Clear(HttpResponse response) => response.Cookies.Delete(Name, Options(null));

    private CookieOptions Options(DateTimeOffset? expires) => new()
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = Path,
        IsEssential = true,
        Expires = expires
    };
}
