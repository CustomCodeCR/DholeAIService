using CustomCodeFramework.Core.Pagination;
using CustomCodeFramework.Postgres.EntityFramework.Repositories;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Contracts.Executions.Response;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Executions.Entities;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Persistence.DbContexts;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Persistence.Repositories;

public sealed class AiExecutionRepository(ServiceDbContext dbContext)
    : EfRepository<AiExecution, Guid>(dbContext),
        IAiExecutionRepository
{
    public override Task<AiExecution?> GetByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        return dbContext
            .AiExecutions.Include(execution => execution.Attempts)
            .SingleOrDefaultAsync(execution => execution.Id == id, cancellationToken);
    }

    public async Task<AiExecutionDto?> GetDtoByIdAsync(
        Guid id,
        CancellationToken cancellationToken = default
    )
    {
        var header = await (
            from execution in dbContext.AiExecutions.AsNoTracking()
            join profile in dbContext.AiProfiles.AsNoTracking()
                on execution.ProfileId equals profile.Id
            join promptTemplate in dbContext.AiPromptTemplates.AsNoTracking()
                on execution.PromptTemplateId equals promptTemplate.Id
                into promptTemplates
            from promptTemplate in promptTemplates.DefaultIfEmpty()
            join selectedConnection in dbContext.AiConnections.AsNoTracking()
                on execution.SelectedConnectionId equals selectedConnection.Id
                into selectedConnections
            from selectedConnection in selectedConnections.DefaultIfEmpty()
            join selectedModel in dbContext.AiModels.AsNoTracking()
                on execution.SelectedModelId equals selectedModel.Id
                into selectedModels
            from selectedModel in selectedModels.DefaultIfEmpty()
            where execution.Id == id
            select new ExecutionProjection(
                execution.Id,
                execution.ProfileId,
                execution.ProfileKey,
                profile.Name,
                execution.PromptTemplateId,
                promptTemplate == null ? null : promptTemplate.Name,
                execution.ExecutionType,
                execution.Status,
                execution.CorrelationId,
                execution.RequestHash,
                execution.OutputReference,
                execution.SelectedConnectionId,
                selectedConnection == null ? null : selectedConnection.Name,
                execution.SelectedModelId,
                selectedModel == null ? null : selectedModel.Name,
                selectedModel == null ? null : selectedModel.ExternalModelId,
                selectedConnection == null ? null : selectedConnection.ProviderType,
                execution.InputTokens,
                execution.OutputTokens,
                execution.EstimatedCost,
                execution.DurationMilliseconds,
                execution.FinishReason,
                execution.ErrorCode,
                execution.ErrorMessage,
                execution.StartedAtUtc,
                execution.CompletedAtUtc,
                execution.CancelledAtUtc,
                execution.CancellationReason
            )
        ).SingleOrDefaultAsync(cancellationToken);

        if (header is null)
        {
            return null;
        }

        var attemptProjections = await (
            from attempt in dbContext.AiExecutionAttempts.AsNoTracking()
            join connection in dbContext.AiConnections.AsNoTracking()
                on attempt.ConnectionId equals connection.Id
            join model in dbContext.AiModels.AsNoTracking() on attempt.ModelId equals model.Id
            where attempt.ExecutionId == id
            orderby attempt.AttemptNumber
            select new AttemptProjection(
                attempt.Id,
                attempt.AttemptNumber,
                attempt.ConnectionId,
                connection.Name,
                attempt.ModelId,
                model.Name,
                attempt.ProviderType,
                attempt.ExternalModelId,
                attempt.Status,
                attempt.StartedAtUtc,
                attempt.CompletedAtUtc,
                attempt.InputTokens,
                attempt.OutputTokens,
                attempt.EstimatedCost,
                attempt.DurationMilliseconds,
                attempt.FinishReason,
                attempt.ErrorCode,
                attempt.ErrorMessage
            )
        ).ToListAsync(cancellationToken);

        var attempts = attemptProjections.Select(ToAttemptDto).ToArray();

        return new AiExecutionDto(
            header.Id,
            header.ProfileId,
            header.ProfileKey,
            header.ProfileName,
            header.PromptTemplateId,
            header.PromptTemplateName,
            header.ExecutionType.ToString(),
            header.Status.ToString(),
            header.CorrelationId,
            header.RequestHash,
            header.OutputReference,
            header.SelectedConnectionId,
            header.SelectedConnectionName,
            header.SelectedModelId,
            header.SelectedModelName,
            header.SelectedExternalModelId,
            header.SelectedProviderType?.ToString(),
            CreateTokenUsage(header.InputTokens, header.OutputTokens),
            header.EstimatedCost,
            header.DurationMilliseconds,
            header.FinishReason.ToString(),
            header.ErrorCode,
            header.ErrorMessage,
            header.StartedAtUtc,
            header.CompletedAtUtc,
            header.CancelledAtUtc,
            header.CancellationReason,
            attempts
        );
    }

    public async Task<PagedResult<AiExecutionSummaryDto>> GetPagedAsync(
        PageRequest page,
        string? search = null,
        string? profileKey = null,
        AiExecutionType? executionType = null,
        AiExecutionStatus? status = null,
        AiProviderType? providerType = null,
        Guid? modelId = null,
        DateTime? dateFromUtc = null,
        DateTime? dateToUtc = null,
        CancellationToken cancellationToken = default
    )
    {
        var query =
            from execution in dbContext.AiExecutions.AsNoTracking()
            join profile in dbContext.AiProfiles.AsNoTracking()
                on execution.ProfileId equals profile.Id
            join connection in dbContext.AiConnections.AsNoTracking()
                on execution.SelectedConnectionId equals connection.Id
                into connections
            from connection in connections.DefaultIfEmpty()
            join model in dbContext.AiModels.AsNoTracking()
                on execution.SelectedModelId equals model.Id
                into models
            from model in models.DefaultIfEmpty()
            select new
            {
                Execution = execution,
                ProfileName = profile.Name,
                Connection = connection,
                Model = model,
            };

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";

            query = query.Where(item =>
                EF.Functions.ILike(item.Execution.ProfileKey, pattern)
                || EF.Functions.ILike(item.ProfileName, pattern)
                || (
                    item.Execution.CorrelationId != null
                    && EF.Functions.ILike(item.Execution.CorrelationId, pattern)
                )
                || (
                    item.Execution.ErrorCode != null
                    && EF.Functions.ILike(item.Execution.ErrorCode, pattern)
                )
                || (item.Model != null && EF.Functions.ILike(item.Model.Name, pattern))
            );
        }

        if (!string.IsNullOrWhiteSpace(profileKey))
        {
            var normalized = profileKey.Trim().ToLowerInvariant();

            query = query.Where(item => item.Execution.ProfileKey == normalized);
        }

        if (executionType.HasValue)
        {
            query = query.Where(item => item.Execution.ExecutionType == executionType.Value);
        }

        if (status.HasValue)
        {
            query = query.Where(item => item.Execution.Status == status.Value);
        }

        if (providerType.HasValue)
        {
            query = query.Where(item =>
                item.Connection != null && item.Connection.ProviderType == providerType.Value
            );
        }

        if (modelId.HasValue)
        {
            query = query.Where(item => item.Execution.SelectedModelId == modelId.Value);
        }

        if (dateFromUtc.HasValue)
        {
            query = query.Where(item => item.Execution.StartedAtUtc >= dateFromUtc.Value);
        }

        if (dateToUtc.HasValue)
        {
            query = query.Where(item => item.Execution.StartedAtUtc <= dateToUtc.Value);
        }

        var total = await query.LongCountAsync(cancellationToken);

        var projections = await query
            .OrderByDescending(item => item.Execution.StartedAtUtc ?? item.Execution.CreatedAtUtc)
            .Skip(page.Skip)
            .Take(page.PageSize)
            .Select(item => new ExecutionSummaryProjection(
                item.Execution.Id,
                item.Execution.ProfileKey,
                item.ProfileName,
                item.Execution.ExecutionType,
                item.Execution.Status,
                item.Connection == null ? null : item.Connection.ProviderType,
                item.Model == null ? null : item.Model.Name,
                item.Execution.InputTokens,
                item.Execution.OutputTokens,
                item.Execution.EstimatedCost,
                item.Execution.DurationMilliseconds,
                item.Execution.StartedAtUtc,
                item.Execution.CompletedAtUtc,
                item.Execution.ErrorCode
            ))
            .ToListAsync(cancellationToken);

        var items = projections
            .Select(item => new AiExecutionSummaryDto(
                item.Id,
                item.ProfileKey,
                item.ProfileName,
                item.ExecutionType.ToString(),
                item.Status.ToString(),
                item.ProviderType?.ToString(),
                item.ModelName,
                CreateTokenUsage(item.InputTokens, item.OutputTokens),
                item.EstimatedCost,
                item.DurationMilliseconds,
                item.StartedAtUtc,
                item.CompletedAtUtc,
                item.ErrorCode
            ))
            .ToArray();

        return PagedResult<AiExecutionSummaryDto>.Create(
            items,
            page.PageNumber,
            page.PageSize,
            total
        );
    }

    private static AiExecutionAttemptDto ToAttemptDto(AttemptProjection attempt)
    {
        return new AiExecutionAttemptDto(
            attempt.Id,
            attempt.AttemptNumber,
            attempt.ConnectionId,
            attempt.ConnectionName,
            attempt.ModelId,
            attempt.ModelName,
            attempt.ProviderType.ToString(),
            attempt.ExternalModelId,
            attempt.Status.ToString(),
            attempt.StartedAtUtc,
            attempt.CompletedAtUtc,
            CreateTokenUsage(attempt.InputTokens, attempt.OutputTokens),
            attempt.EstimatedCost,
            attempt.DurationMilliseconds,
            attempt.FinishReason.ToString(),
            attempt.ErrorCode,
            attempt.ErrorMessage
        );
    }

    private static AiTokenUsageDto CreateTokenUsage(int inputTokens, int outputTokens)
    {
        return new AiTokenUsageDto(inputTokens, outputTokens, inputTokens + outputTokens);
    }

    private sealed record ExecutionProjection(
        Guid Id,
        Guid ProfileId,
        string ProfileKey,
        string ProfileName,
        Guid? PromptTemplateId,
        string? PromptTemplateName,
        AiExecutionType ExecutionType,
        AiExecutionStatus Status,
        string? CorrelationId,
        string? RequestHash,
        string? OutputReference,
        Guid? SelectedConnectionId,
        string? SelectedConnectionName,
        Guid? SelectedModelId,
        string? SelectedModelName,
        string? SelectedExternalModelId,
        AiProviderType? SelectedProviderType,
        int InputTokens,
        int OutputTokens,
        decimal EstimatedCost,
        long DurationMilliseconds,
        AiFinishReason FinishReason,
        string? ErrorCode,
        string? ErrorMessage,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        DateTime? CancelledAtUtc,
        string? CancellationReason
    );

    private sealed record AttemptProjection(
        Guid Id,
        int AttemptNumber,
        Guid ConnectionId,
        string ConnectionName,
        Guid ModelId,
        string ModelName,
        AiProviderType ProviderType,
        string ExternalModelId,
        AiAttemptStatus Status,
        DateTime StartedAtUtc,
        DateTime? CompletedAtUtc,
        int InputTokens,
        int OutputTokens,
        decimal EstimatedCost,
        long DurationMilliseconds,
        AiFinishReason FinishReason,
        string? ErrorCode,
        string? ErrorMessage
    );

    private sealed record ExecutionSummaryProjection(
        Guid Id,
        string ProfileKey,
        string ProfileName,
        AiExecutionType ExecutionType,
        AiExecutionStatus Status,
        AiProviderType? ProviderType,
        string? ModelName,
        int InputTokens,
        int OutputTokens,
        decimal EstimatedCost,
        long DurationMilliseconds,
        DateTime? StartedAtUtc,
        DateTime? CompletedAtUtc,
        string? ErrorCode
    );
}
