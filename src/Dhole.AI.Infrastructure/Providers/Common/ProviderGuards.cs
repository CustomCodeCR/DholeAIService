using Dhole.AI.Application.Abstractions.Providers.Models;

namespace Dhole.AI.Infrastructure.Providers.Common;

internal static class ProviderGuards
{
    public static string RequireExternalModelId(AiProviderContext context)
    {
        if (string.IsNullOrWhiteSpace(context.ExternalModelId))
        {
            throw new InvalidOperationException(
                "No se indicó el modelo externo " + "para la ejecución."
            );
        }

        return context.ExternalModelId;
    }
}
