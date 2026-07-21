using Dhole.AI.Domain.Connections.Entities;
using Dhole.AI.Domain.Executions.Entities;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.PromptTemplates.Entities;

namespace Dhole.AI.Application.Auditing;

public static class AiAuditSnapshots
{
    public static object From(AiConnection connection) =>
        new
        {
            connection.Id,
            connection.Name,
            ProviderType = connection.ProviderType.ToString(),
            connection.BaseUrl,
            connection.SecretReference,
            connection.TimeoutSeconds,
            Status = connection.Status.ToString(),
            connection.LastHealthCheckAtUtc,
            connection.LastHealthError,
            connection.IsActive,
            connection.IsDeleted,
        };

    public static object From(AiModel model) =>
        new
        {
            model.Id,
            model.ConnectionId,
            model.ExternalModelId,
            model.Name,
            Capabilities = model.Capabilities.ToString(),
            model.ContextWindow,
            model.MaximumOutputTokens,
            model.InputCostPerMillionTokens,
            model.OutputCostPerMillionTokens,
            model.IsLocal,
            Status = model.Status.ToString(),
            model.LastAvailabilityCheckAtUtc,
            model.LastAvailabilityError,
            model.IsActive,
            model.IsDeleted,
        };

    public static object From(AiProfile profile) =>
        new
        {
            profile.Id,
            profile.Key,
            profile.Name,
            profile.Description,
            profile.PromptTemplateId,
            RoutingMode = profile.RoutingMode.ToString(),
            ResponseFormat = profile.ResponseFormat.ToString(),
            profile.Temperature,
            profile.MaximumOutputTokens,
            profile.TimeoutSeconds,
            profile.JsonSchema,
            profile.IsActive,
            profile.IsDeleted,
            Models = profile
                .Models.OrderBy(item => item.Priority)
                .Select(item => new
                {
                    item.Id,
                    item.ModelId,
                    item.Priority,
                    item.IsFallback,
                })
                .ToArray(),
        };

    public static object From(AiPromptTemplate template) =>
        new
        {
            template.Id,
            template.Key,
            template.Name,
            template.Description,
            template.SystemPrompt,
            template.UserPromptTemplate,
            template.VariablesJson,
            template.IsActive,
            template.IsDeleted,
        };

    public static object From(AiExecution execution) =>
        new
        {
            execution.Id,
            execution.ProfileId,
            execution.ProfileKey,
            execution.PromptTemplateId,
            ExecutionType = execution.ExecutionType.ToString(),
            Status = execution.Status.ToString(),
            execution.CorrelationId,
            execution.RequestHash,
            execution.OutputReference,
            execution.SelectedConnectionId,
            execution.SelectedModelId,
            execution.InputTokens,
            execution.OutputTokens,
            execution.EstimatedCost,
            execution.DurationMilliseconds,
            FinishReason = execution.FinishReason.ToString(),
            execution.ErrorCode,
            execution.ErrorMessage,
            execution.StartedAtUtc,
            execution.CompletedAtUtc,
            execution.CancelledAtUtc,
            execution.CancellationReason,
            Attempts = execution
                .Attempts.OrderBy(item => item.AttemptNumber)
                .Select(item => new
                {
                    item.Id,
                    item.AttemptNumber,
                    item.ConnectionId,
                    item.ModelId,
                    ProviderType = item.ProviderType.ToString(),
                    item.ExternalModelId,
                    Status = item.Status.ToString(),
                    item.StartedAtUtc,
                    item.CompletedAtUtc,
                    item.InputTokens,
                    item.OutputTokens,
                    item.EstimatedCost,
                    item.DurationMilliseconds,
                    FinishReason = item.FinishReason.ToString(),
                    item.ErrorCode,
                    item.ErrorMessage,
                })
                .ToArray(),
        };
}
