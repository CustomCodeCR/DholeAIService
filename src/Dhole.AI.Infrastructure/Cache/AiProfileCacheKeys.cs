namespace Dhole.AI.Infrastructure.Cache;

internal static class AiProfileCacheKeys
{
    public static string ById(Guid id)
    {
        return $"ai:profiles:id:v1:{id:N}";
    }

    public static string ByKey(string key)
    {
        var normalized = key.Trim().ToLowerInvariant();

        return $"ai:profiles:key:v1:{normalized}";
    }
}
