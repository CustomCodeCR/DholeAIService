using CustomCodeFramework.Core.Domain.Entities;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Domain.Executions.Events;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Domain.Executions.Entities;

public sealed class AiExecution : AuditableAggregateRoot<Guid>
{
    private readonly List<AiExecutionAttempt> _attempts = [];

    private AiExecution() { }

    private AiExecution(
        Guid id,
        Guid profileId,
        string profileKey,
        Guid? promptTemplateId,
        AiExecutionType executionType,
        string? correlationId,
        string? requestHash,
        Guid? requestedBy
    )
        : base(id)
    {
        if (profileId == Guid.Empty)
        {
            throw new InvalidOperationException("El perfil de ejecución es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(profileKey))
        {
            throw new InvalidOperationException("La clave del perfil es obligatoria.");
        }

        ProfileId = profileId;
        ProfileKey = profileKey.Trim().ToLowerInvariant();

        PromptTemplateId = promptTemplateId == Guid.Empty ? null : promptTemplateId;

        ExecutionType = executionType;
        Status = AiExecutionStatus.Pending;
        CorrelationId = Normalize(correlationId);
        RequestHash = Normalize(requestHash);
        FinishReason = AiFinishReason.Unknown;

        MarkAsCreated(DateTime.UtcNow, requestedBy?.ToString());
    }

    public Guid ProfileId { get; private set; }

    public string ProfileKey { get; private set; } = string.Empty;

    public Guid? PromptTemplateId { get; private set; }

    public AiExecutionType ExecutionType { get; private set; }

    public AiExecutionStatus Status { get; private set; }

    public string? CorrelationId { get; private set; }

    public string? RequestHash { get; private set; }

    public string? OutputReference { get; private set; }

    public Guid? SelectedConnectionId { get; private set; }

    public Guid? SelectedModelId { get; private set; }

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public decimal EstimatedCost { get; private set; }

    public long DurationMilliseconds { get; private set; }

    public AiFinishReason FinishReason { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public DateTime? CancelledAtUtc { get; private set; }

    public string? CancellationReason { get; private set; }

    public IReadOnlyCollection<AiExecutionAttempt> Attempts => _attempts.AsReadOnly();

    public static AiExecution Create(
        Guid profileId,
        string profileKey,
        Guid? promptTemplateId,
        AiExecutionType executionType,
        string? correlationId,
        string? requestHash,
        Guid? requestedBy
    )
    {
        return new AiExecution(
            Guid.NewGuid(),
            profileId,
            profileKey,
            promptTemplateId,
            executionType,
            correlationId,
            requestHash,
            requestedBy
        );
    }

    public void Start(Guid? requestedBy)
    {
        if (Status != AiExecutionStatus.Pending)
        {
            throw new InvalidOperationException("Solo una ejecución pendiente puede iniciarse.");
        }

        Status = AiExecutionStatus.Running;
        StartedAtUtc = DateTime.UtcNow;

        MarkAsUpdated(StartedAtUtc.Value, requestedBy?.ToString());

        AddDomainEvent(
            new AiExecutionStartedDomainEvent(Id, ProfileId, ProfileKey, ExecutionType, requestedBy)
        );
    }

    public AiExecutionAttempt StartAttempt(
        Guid connectionId,
        Guid modelId,
        AiProviderType providerType,
        string externalModelId
    )
    {
        EnsureRunning();

        if (_attempts.Any(item => item.Status == AiAttemptStatus.Running))
        {
            throw new InvalidOperationException("Ya existe un intento en ejecución.");
        }

        var attempt = AiExecutionAttempt.Start(
            Id,
            _attempts.Count + 1,
            connectionId,
            modelId,
            providerType,
            externalModelId,
            DateTime.UtcNow
        );

        _attempts.Add(attempt);

        return attempt;
    }

    public void CompleteAttempt(
        Guid attemptId,
        int inputTokens,
        int outputTokens,
        decimal estimatedCost,
        long durationMilliseconds,
        AiFinishReason finishReason
    )
    {
        EnsureRunning();

        var attempt = GetAttempt(attemptId);

        attempt.Complete(
            inputTokens,
            outputTokens,
            estimatedCost,
            durationMilliseconds,
            finishReason,
            DateTime.UtcNow
        );
    }

    public void FailAttempt(
        Guid attemptId,
        string errorCode,
        string errorMessage,
        long durationMilliseconds
    )
    {
        EnsureRunning();

        var attempt = GetAttempt(attemptId);

        attempt.Fail(errorCode, errorMessage, durationMilliseconds, DateTime.UtcNow);
    }

    public void CancelAttempt(Guid attemptId, long durationMilliseconds)
    {
        EnsureRunning();

        var attempt = GetAttempt(attemptId);

        attempt.Cancel(durationMilliseconds, DateTime.UtcNow);
    }

    public void RegisterFallback(Guid previousModelId, Guid nextModelId, string reason)
    {
        EnsureRunning();

        if (previousModelId == Guid.Empty || nextModelId == Guid.Empty)
        {
            throw new InvalidOperationException("Los modelos del fallback son obligatorios.");
        }

        if (previousModelId == nextModelId)
        {
            throw new InvalidOperationException(
                "El modelo de fallback debe ser diferente al anterior."
            );
        }

        if (string.IsNullOrWhiteSpace(reason))
        {
            throw new InvalidOperationException("El motivo del fallback es obligatorio.");
        }

        AddDomainEvent(
            new AiExecutionFallbackUsedDomainEvent(Id, previousModelId, nextModelId, reason.Trim())
        );
    }

    public void Complete(Guid successfulAttemptId, string? outputReference, Guid? updatedBy)
    {
        EnsureRunning();

        var attempt = GetAttempt(successfulAttemptId);

        if (attempt.Status != AiAttemptStatus.Completed)
        {
            throw new InvalidOperationException("El intento seleccionado no fue completado.");
        }

        if (_attempts.Any(item => item.Status == AiAttemptStatus.Running))
        {
            throw new InvalidOperationException(
                "No se puede completar mientras exista " + "un intento activo."
            );
        }

        Status = AiExecutionStatus.Completed;
        SelectedConnectionId = attempt.ConnectionId;
        SelectedModelId = attempt.ModelId;
        InputTokens = attempt.InputTokens;
        OutputTokens = attempt.OutputTokens;
        EstimatedCost = attempt.EstimatedCost;
        DurationMilliseconds = CalculateTotalDuration();
        FinishReason = attempt.FinishReason;
        OutputReference = Normalize(outputReference);
        ErrorCode = null;
        ErrorMessage = null;
        CompletedAtUtc = DateTime.UtcNow;

        MarkAsUpdated(CompletedAtUtc.Value, updatedBy?.ToString());

        AddDomainEvent(
            new AiExecutionCompletedDomainEvent(
                Id,
                SelectedConnectionId,
                SelectedModelId,
                InputTokens,
                OutputTokens,
                EstimatedCost,
                DurationMilliseconds,
                FinishReason
            )
        );
    }

    public void Fail(string errorCode, string errorMessage, Guid? updatedBy)
    {
        if (Status is AiExecutionStatus.Completed or AiExecutionStatus.Cancelled)
        {
            throw new InvalidOperationException("La ejecución ya fue finalizada.");
        }

        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new InvalidOperationException("El código de error es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new InvalidOperationException("El mensaje de error es obligatorio.");
        }

        foreach (var attempt in _attempts.Where(item => item.Status == AiAttemptStatus.Running))
        {
            attempt.Fail(
                errorCode,
                errorMessage,
                CalculateRunningDuration(attempt),
                DateTime.UtcNow
            );
        }

        Status = AiExecutionStatus.Failed;
        FinishReason = AiFinishReason.Error;
        ErrorCode = errorCode.Trim();

        ErrorMessage = Truncate(errorMessage.Trim(), AiConstants.MaximumErrorLength);

        DurationMilliseconds = CalculateTotalDuration();
        CompletedAtUtc = DateTime.UtcNow;

        MarkAsUpdated(CompletedAtUtc.Value, updatedBy?.ToString());

        AddDomainEvent(
            new AiExecutionFailedDomainEvent(Id, ErrorCode, ErrorMessage, DurationMilliseconds)
        );
    }

    public void Cancel(string? reason, Guid? cancelledBy)
    {
        if (
            Status
            is AiExecutionStatus.Completed
                or AiExecutionStatus.Failed
                or AiExecutionStatus.Cancelled
        )
        {
            throw new InvalidOperationException("La ejecución ya fue finalizada.");
        }

        foreach (var attempt in _attempts.Where(item => item.Status == AiAttemptStatus.Running))
        {
            attempt.Cancel(CalculateRunningDuration(attempt), DateTime.UtcNow);
        }

        Status = AiExecutionStatus.Cancelled;
        FinishReason = AiFinishReason.Cancelled;
        CancellationReason = Normalize(reason);
        CancelledAtUtc = DateTime.UtcNow;
        CompletedAtUtc = CancelledAtUtc;
        DurationMilliseconds = CalculateTotalDuration();

        MarkAsUpdated(CancelledAtUtc.Value, cancelledBy?.ToString());

        AddDomainEvent(new AiExecutionCancelledDomainEvent(Id, CancellationReason, cancelledBy));
    }

    private AiExecutionAttempt GetAttempt(Guid attemptId)
    {
        var attempt = _attempts.SingleOrDefault(item => item.Id == attemptId);

        return attempt ?? throw new InvalidOperationException("El intento de ejecución no existe.");
    }

    private void EnsureRunning()
    {
        if (Status != AiExecutionStatus.Running)
        {
            throw new InvalidOperationException("La ejecución no se encuentra en proceso.");
        }
    }

    private long CalculateTotalDuration()
    {
        if (!StartedAtUtc.HasValue)
        {
            return 0L;
        }

        return Math.Max(0L, (long)(DateTime.UtcNow - StartedAtUtc.Value).TotalMilliseconds);
    }

    private static long CalculateRunningDuration(AiExecutionAttempt attempt)
    {
        return Math.Max(0L, (long)(DateTime.UtcNow - attempt.StartedAtUtc).TotalMilliseconds);
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
