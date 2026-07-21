using CustomCodeFramework.Core.Domain.Entities;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Executions.Enums;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Domain.Executions.Entities;

public sealed class AiExecutionAttempt : Entity<Guid>
{
    private AiExecutionAttempt() { }

    private AiExecutionAttempt(
        Guid id,
        Guid executionId,
        int attemptNumber,
        Guid connectionId,
        Guid modelId,
        AiProviderType providerType,
        string externalModelId,
        DateTime startedAtUtc
    )
        : base(id)
    {
        if (executionId == Guid.Empty)
        {
            throw new InvalidOperationException("La ejecución es obligatoria.");
        }

        if (attemptNumber <= 0)
        {
            throw new InvalidOperationException("El número de intento debe ser mayor que cero.");
        }

        if (connectionId == Guid.Empty)
        {
            throw new InvalidOperationException("La conexión del intento es obligatoria.");
        }

        if (modelId == Guid.Empty)
        {
            throw new InvalidOperationException("El modelo del intento es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(externalModelId))
        {
            throw new InvalidOperationException(
                "El identificador externo del modelo es obligatorio."
            );
        }

        ExecutionId = executionId;
        AttemptNumber = attemptNumber;
        ConnectionId = connectionId;
        ModelId = modelId;
        ProviderType = providerType;
        ExternalModelId = externalModelId.Trim();
        Status = AiAttemptStatus.Running;
        StartedAtUtc = startedAtUtc;
        FinishReason = AiFinishReason.Unknown;
    }

    public Guid ExecutionId { get; private set; }

    public int AttemptNumber { get; private set; }

    public Guid ConnectionId { get; private set; }

    public Guid ModelId { get; private set; }

    public AiProviderType ProviderType { get; private set; }

    public string ExternalModelId { get; private set; } = string.Empty;

    public AiAttemptStatus Status { get; private set; }

    public DateTime StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public int InputTokens { get; private set; }

    public int OutputTokens { get; private set; }

    public decimal EstimatedCost { get; private set; }

    public long DurationMilliseconds { get; private set; }

    public AiFinishReason FinishReason { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    internal static AiExecutionAttempt Start(
        Guid executionId,
        int attemptNumber,
        Guid connectionId,
        Guid modelId,
        AiProviderType providerType,
        string externalModelId,
        DateTime startedAtUtc
    )
    {
        return new AiExecutionAttempt(
            Guid.NewGuid(),
            executionId,
            attemptNumber,
            connectionId,
            modelId,
            providerType,
            externalModelId,
            startedAtUtc
        );
    }

    internal void Complete(
        int inputTokens,
        int outputTokens,
        decimal estimatedCost,
        long durationMilliseconds,
        AiFinishReason finishReason,
        DateTime completedAtUtc
    )
    {
        EnsureRunning();

        ValidateMetrics(inputTokens, outputTokens, estimatedCost, durationMilliseconds);

        Status = AiAttemptStatus.Completed;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
        EstimatedCost = estimatedCost;
        DurationMilliseconds = durationMilliseconds;
        FinishReason = finishReason;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = null;
        ErrorMessage = null;
    }

    internal void Fail(
        string errorCode,
        string errorMessage,
        long durationMilliseconds,
        DateTime completedAtUtc
    )
    {
        EnsureRunning();

        if (string.IsNullOrWhiteSpace(errorCode))
        {
            throw new InvalidOperationException("El código de error es obligatorio.");
        }

        if (string.IsNullOrWhiteSpace(errorMessage))
        {
            throw new InvalidOperationException("El mensaje de error es obligatorio.");
        }

        if (durationMilliseconds < 0)
        {
            throw new InvalidOperationException("La duración no puede ser negativa.");
        }

        Status = AiAttemptStatus.Failed;
        DurationMilliseconds = durationMilliseconds;
        FinishReason = AiFinishReason.Error;
        CompletedAtUtc = completedAtUtc;
        ErrorCode = errorCode.Trim();

        ErrorMessage = Truncate(errorMessage.Trim(), AiConstants.MaximumErrorLength);
    }

    internal void Cancel(long durationMilliseconds, DateTime completedAtUtc)
    {
        EnsureRunning();

        if (durationMilliseconds < 0)
        {
            throw new InvalidOperationException("La duración no puede ser negativa.");
        }

        Status = AiAttemptStatus.Cancelled;
        DurationMilliseconds = durationMilliseconds;
        FinishReason = AiFinishReason.Cancelled;
        CompletedAtUtc = completedAtUtc;
    }

    private void EnsureRunning()
    {
        if (Status != AiAttemptStatus.Running)
        {
            throw new InvalidOperationException("El intento ya fue finalizado.");
        }
    }

    private static void ValidateMetrics(
        int inputTokens,
        int outputTokens,
        decimal estimatedCost,
        long durationMilliseconds
    )
    {
        if (inputTokens < 0 || outputTokens < 0)
        {
            throw new InvalidOperationException("La cantidad de tokens no puede ser negativa.");
        }

        if (estimatedCost < 0m)
        {
            throw new InvalidOperationException("El costo estimado no puede ser negativo.");
        }

        if (durationMilliseconds < 0)
        {
            throw new InvalidOperationException("La duración no puede ser negativa.");
        }
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
