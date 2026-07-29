namespace Dhole.AI.Contracts.Executions.Request;

public sealed record AiImageRequest(string MimeType, string Base64Data);

public sealed record AiMessageRequest(
    string Role,
    string Content,
    IReadOnlyCollection<AiImageRequest>? Images = null
);
