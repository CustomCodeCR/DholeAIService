
using Dhole.AI.Contracts.Executions.Request;
using Dhole.AI.Contracts.Executions.Response;
using Dhole.AI.Contracts.Grpc;

namespace Dhole.AI.Api.Grpc;

internal static class AiGrpcMappings
{
    public static IReadOnlyCollection<AiMessageRequest> ToMessages(
        IEnumerable<AiMessageGrpcModel> messages
    )
    {
        return messages.Select(x => new AiMessageRequest(x.Role, x.Content)).ToArray();
    }

    public static IReadOnlyCollection<AiPromptVariableRequest> ToVariables(
        IEnumerable<AiPromptVariableGrpcModel> variables
    )
    {
        return variables.Select(x => new AiPromptVariableRequest(x.Name, x.Value)).ToArray();
    }

    public static Guid? ParseOptionalGuid(string? value)
    {
        return Guid.TryParse(value, out var parsed) ? parsed : null;
    }

    public static AiTokenUsageGrpcModel ToGrpc(AiTokenUsageDto usage)
    {
        return new AiTokenUsageGrpcModel
        {
            InputTokens = usage.InputTokens,
            OutputTokens = usage.OutputTokens,
            TotalTokens = usage.TotalTokens,
        };
    }

    public static AiSelectedModelGrpcModel ToSelectedModel(AiChatResultDto result)
    {
        return new AiSelectedModelGrpcModel
        {
            ConnectionId = result.ConnectionId.ToString(),
            ConnectionName = result.ConnectionName,
            ModelId = result.ModelId.ToString(),
            ModelName = result.ModelName,
            ExternalModelId = result.ExternalModelId,
            ProviderType = result.ProviderType,
        };
    }

    public static AiSelectedModelGrpcModel ToSelectedModel(AiStructuredResultDto result)
    {
        return new AiSelectedModelGrpcModel
        {
            ConnectionId = result.ConnectionId.ToString(),
            ConnectionName = result.ConnectionName,
            ModelId = result.ModelId.ToString(),
            ModelName = result.ModelName,
            ExternalModelId = result.ExternalModelId,
            ProviderType = result.ProviderType,
        };
    }

    public static AiSelectedModelGrpcModel ToSelectedModel(AiEmbeddingsResultDto result)
    {
        return new AiSelectedModelGrpcModel
        {
            ConnectionId = result.ConnectionId.ToString(),
            ConnectionName = result.ConnectionName,
            ModelId = result.ModelId.ToString(),
            ModelName = result.ModelName,
            ExternalModelId = result.ExternalModelId,
            ProviderType = result.ProviderType,
        };
    }

    public static string ToGrpcDate(DateTime? value)
    {
        return value?.ToUniversalTime().ToString("O") ?? string.Empty;
    }
}
