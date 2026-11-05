using Microsoft.AspNetCore.Http;

namespace Platform.Auth.Controllers;

/// <summary>
/// The cookie that holds a signed-in person's access token for its whole life (<c>Jwt:AccessTokenDays</c>). There is no
/// refresh token, so this is the only credential. HttpOnly so scripts on the page can never read it, Secure and
/// SameSite=Strict so it travels only over HTTPS and never on a cross-site request, and scoped to a path so the two kinds
/// of session stay apart: members' goes to <c>/v1</c> and Site admins' only to <c>/v1/site</c>. A request under
/// <c>/v1/site</c> is judged by the Site admin cookie alone and every other request by the member cookie alone
/// (<see cref="ForRequest"/>), so neither session can stand in for the other.
/// </summary>
internal sealed class AccessTokenCookie(string name, string path)
{
    private static readonly PathString SitePath = new("/v1/site");

    /// <summary>Organization members: sent to the whole API, but never read on a Site admin route.</summary>
    public static readonly AccessTokenCookie Member = new("access_token", "/v1");

    /// <summary>Site admins: sent only to /v1/site.</summary>
    public static readonly AccessTokenCookie Site = new("site_access_token", "/v1/site");

    public string Name { get; } = name;
    public string Path { get; } = path;

    /// <summary>The cookie that may authenticate a request to <paramref name="requestPath"/>.</summary>
    public static AccessTokenCookie ForRequest(PathString requestPath) =>
        requestPath.StartsWithSegments(SitePath, StringComparison.OrdinalIgnoreCase) ? Site : Member;

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
