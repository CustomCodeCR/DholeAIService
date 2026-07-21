namespace Dhole.AI.Infrastructure.Cache;

internal static class AiPromptTemplateCacheKeys
{
    public static string ById(Guid id)
    {
        return $"ai:prompt-templates:id:v1:{id:N}";
    }
}
