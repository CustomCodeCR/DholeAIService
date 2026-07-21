namespace Dhole.AI.Infrastructure.Cache;

internal static class AiModelCacheKeys
{
    public static string ById(Guid id)
    {
        return $"ai:models:id:v1:{id:N}";
    }

    public static string ConnectionModelsVersion(Guid connectionId)
    {
        return $"ai:models:connection:" + $"{connectionId:N}:version";
    }
}
