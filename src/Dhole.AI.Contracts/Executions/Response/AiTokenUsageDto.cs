namespace Dhole.AI.Contracts.Executions.Response;

public sealed record AiTokenUsageDto(int InputTokens, int OutputTokens, int TotalTokens);
