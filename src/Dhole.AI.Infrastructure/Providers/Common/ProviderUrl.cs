namespace Dhole.AI.Infrastructure.Providers.Common;

internal static class ProviderUrl
{
    public static string Combine(string baseUrl, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        return $"{baseUrl.TrimEnd('/')}/" + relativePath.TrimStart('/');
    }

    public static string Api(string baseUrl, string version, string relativePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var normalizedBase = baseUrl.TrimEnd('/');
        var normalizedVersion = version.Trim('/');
        var normalizedRelative = relativePath.TrimStart('/');

        if (
            Uri.TryCreate(normalizedBase, UriKind.Absolute, out var uri)
            && uri.AbsolutePath.TrimEnd('/')
                .EndsWith($"/{normalizedVersion}", StringComparison.OrdinalIgnoreCase)
        )
        {
            return $"{normalizedBase}/{normalizedRelative}";
        }

        return $"{normalizedBase}/" + $"{normalizedVersion}/" + normalizedRelative;
    }
}
