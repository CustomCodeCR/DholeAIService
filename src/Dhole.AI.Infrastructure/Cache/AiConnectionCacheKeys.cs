namespace Dhole.AI.Infrastructure.Cache;

internal static class AiConnectionCacheKeys
{
    public static string ById(Guid id)
    {
        return $"ai:connections:id:v1:{id:N}";
    }
}
