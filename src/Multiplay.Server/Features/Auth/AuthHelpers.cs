namespace Multiplay.Server.Features.Auth;

internal static class AuthHelpers
{
    internal static string? ExtractBearerToken(HttpContext http) =>
        http.Request.Headers.Authorization
            .FirstOrDefault()
            ?.Split(' ', 2)
            .LastOrDefault();
}
