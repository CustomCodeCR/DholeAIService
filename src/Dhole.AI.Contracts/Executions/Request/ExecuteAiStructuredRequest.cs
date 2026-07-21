namespace Dhole.AI.Contracts.Executions.Request;

public sealed record ExecuteAiStructuredRequest(
    string ProfileKey,
    IReadOnlyCollection<AiMessageRequest> Messages,
    IReadOnlyCollection<AiPromptVariableRequest>? Variables = null,
    string? JsonSchemaOverride = null,
    string? CorrelationId = null,
    string? RequestHash = null
);
