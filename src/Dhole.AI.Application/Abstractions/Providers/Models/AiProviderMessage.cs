namespace Dhole.AI.Application.Abstractions.Providers.Models;

public sealed record AiProviderImage(string MimeType, string Base64Data);

public sealed record AiProviderMessage(
    string Role,
    string Content,
    IReadOnlyCollection<AiProviderImage>? Images = null
);
