using Microsoft.AspNetCore.Http;

namespace NetValve.Server.Runtime;

internal static class AuthTokenResolver
{
    public static string? Resolve(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue("X-Auth-Token", out var headerToken))
        {
            var value = headerToken.FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(value))
                return value;
        }

        if (context.Request.Headers.TryGetValue("Authorization", out var authHeader))
        {
            var value = authHeader.FirstOrDefault();
            const string bearerPrefix = "Bearer ";
            if (!string.IsNullOrWhiteSpace(value) && value.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
                return value[bearerPrefix.Length..].Trim();
        }

        return null;
    }
}
