
namespace Dhole.AI.Api.Extensions;

public static class HttpContextAuditExtensions
{
    public static Guid? GetCurrentUserId(this HttpContext httpContext)
    {
        var value =
            httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.User.FindFirst("user_id")?.Value
            ?? httpContext.User.FindFirst("nameidentifier")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        return Guid.TryParse(value, out var userId) ? userId : null;
    }

    public static string? GetCurrentUserName(this HttpContext httpContext)
    {
        return httpContext.User.FindFirst("name")?.Value
            ?? httpContext.User.FindFirst("preferred_username")?.Value
            ?? httpContext.User.FindFirst("username")?.Value
            ?? httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Name)?.Value;
    }

    public static bool HasRole(this HttpContext httpContext, params string[] roles)
    {
        if (roles.Length == 0)
        {
            return false;
        }

        return httpContext.User.Claims.Any(claim =>
            IsRoleClaim(claim.Type)
            && claim
                .Value.Split(
                    new[] { ' ', ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Any(role => roles.Contains(role, StringComparer.OrdinalIgnoreCase))
        );
    }

    public static bool HasScope(this HttpContext httpContext, string scope)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        return httpContext.User.Claims.Any(claim =>
            IsScopeClaim(claim.Type)
            && claim
                .Value.Split(
                    new[] { ' ', ',', ';' },
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                )
                .Contains(scope, StringComparer.OrdinalIgnoreCase)
        );
    }

    private static bool IsRoleClaim(string claimType)
    {
        return claimType.Equals("role", StringComparison.OrdinalIgnoreCase)
            || claimType.Equals("roles", StringComparison.OrdinalIgnoreCase)
            || claimType.EndsWith("/role", StringComparison.OrdinalIgnoreCase)
            || claimType.EndsWith("/roles", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsScopeClaim(string claimType)
    {
        return claimType.Equals("scope", StringComparison.OrdinalIgnoreCase)
            || claimType.Equals("scp", StringComparison.OrdinalIgnoreCase)
            || claimType.Equals("scopes", StringComparison.OrdinalIgnoreCase)
            || claimType.EndsWith("/scope", StringComparison.OrdinalIgnoreCase)
            || claimType.EndsWith("/scopes", StringComparison.OrdinalIgnoreCase);
    }
}
