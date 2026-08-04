using CustomCodeFramework.Core.Domain.Entities;
using Dhole.AI.Domain.EmailAnalysis.Enums;

namespace Dhole.AI.Domain.EmailAnalysis.Entities;

public sealed class AiEmailAnalysisJob : AuditableAggregateRoot<Guid>
{
    private AiEmailAnalysisJob() { }

    private AiEmailAnalysisJob(
        Guid id,
        Guid externalRequestId,
        Guid emailExtractionJobId,
        Guid emailMessageId,
        Guid? emailAttachmentId,
        string payloadUrl,
        string requestHash,
        string correlationId,
        int maxAttemptCount
    )
        : base(id)
    {
        ExternalRequestId = externalRequestId;
        EmailExtractionJobId = emailExtractionJobId;
        EmailMessageId = emailMessageId;
        EmailAttachmentId = emailAttachmentId;
        PayloadUrl = Required(payloadUrl, "La URL del payload es requerida.");
        RequestHash = Required(requestHash, "El RequestHash es requerido.");
        CorrelationId = Required(correlationId, "El CorrelationId es requerido.");
        MaxAttemptCount = Math.Max(1, maxAttemptCount);
        Status = AiEmailAnalysisJobStatus.Pending;

        MarkAsCreated(DateTime.UtcNow, null);
    }

    public Guid ExternalRequestId { get; private set; }

    public Guid EmailExtractionJobId { get; private set; }

    public Guid EmailMessageId { get; private set; }

    public Guid? EmailAttachmentId { get; private set; }

    public string PayloadUrl { get; private set; } = string.Empty;

    public string RequestHash { get; private set; } = string.Empty;

    public string CorrelationId { get; private set; } = string.Empty;

    public AiEmailAnalysisJobStatus Status { get; private set; }

    public int AttemptCount { get; private set; }

    public int MaxAttemptCount { get; private set; }

    public DateTime? NextAttemptAtUtc { get; private set; }

    public string? LeaseOwner { get; private set; }

    public DateTime? LeaseExpiresAtUtc { get; private set; }

    public DateTime? LastHeartbeatAtUtc { get; private set; }

    public Guid? AiExecutionId { get; private set; }

    public string? ResultJson { get; private set; }

    public string? ErrorCode { get; private set; }

    public string? ErrorMessage { get; private set; }

    public DateTime? StartedAtUtc { get; private set; }

    public DateTime? CompletedAtUtc { get; private set; }

    public int Version { get; private set; } = 1;

    public static AiEmailAnalysisJob Create(
        Guid externalRequestId,
        Guid emailExtractionJobId,
        Guid emailMessageId,
        Guid? emailAttachmentId,
        string payloadUrl,
        string requestHash,
        string correlationId,
        int maxAttemptCount
    )
    {
        if (
            externalRequestId == Guid.Empty
            || emailExtractionJobId == Guid.Empty
            || emailMessageId == Guid.Empty
        )
        {
            throw new InvalidOperationException(
                "La solicitud, el trabajo de extracción y el correo son requeridos."
            );
        }

        return new AiEmailAnalysisJob(
            Guid.NewGuid(),
            externalRequestId,
            emailExtractionJobId,
            emailMessageId,
            emailAttachmentId,
            payloadUrl,
            requestHash,
            correlationId,
            maxAttemptCount
        );
    }

    public void MarkProcessing(string leaseOwner, DateTime leaseExpiresAtUtc)
    {
        if (
            Status
            is not AiEmailAnalysisJobStatus.Pending
                and not AiEmailAnalysisJobStatus.RetryScheduled
        )
        {
            throw new InvalidOperationException(
                "Solo un trabajo pendiente o reprogramado puede reclamarse."
            );
        }

        if (string.IsNullOrWhiteSpace(leaseOwner))
        {
            throw new InvalidOperationException("El propietario del lease es requerido.");
        }

        var now = DateTime.UtcNow;
        Status = AiEmailAnalysisJobStatus.Processing;
        AttemptCount++;
        NextAttemptAtUtc = null;
        LeaseOwner = leaseOwner.Trim();
        LeaseExpiresAtUtc = leaseExpiresAtUtc > now
            ? leaseExpiresAtUtc
            : now.AddMinutes(30);
        LastHeartbeatAtUtc = now;
        StartedAtUtc ??= now;
        ErrorCode = null;
        ErrorMessage = null;
        Touch(now);
    }

    public void MarkCompleted(Guid aiExecutionId, string resultJson)
    {
        if (Status == AiEmailAnalysisJobStatus.Completed)
        {
            return;
        }

        if (Status != AiEmailAnalysisJobStatus.Processing)
        {
            throw new InvalidOperationException(
                "Solo un trabajo en procesamiento puede completarse."
            );
        }

        if (aiExecutionId == Guid.Empty || string.IsNullOrWhiteSpace(resultJson))
        {
            throw new InvalidOperationException(
                "La ejecución y el resultado de AI son requeridos."
            );
        }

        AiExecutionId = aiExecutionId;
        ResultJson = resultJson;
        Status = AiEmailAnalysisJobStatus.Completed;
        ErrorCode = null;
        ErrorMessage = null;
        CompletedAtUtc = DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(CompletedAtUtc.Value);
    }

    public void ScheduleRetry(
        string errorCode,
        string errorMessage,
        DateTime nextAttemptAtUtc,
        Guid? aiExecutionId = null
    )
    {
        if (Status != AiEmailAnalysisJobStatus.Processing)
        {
            throw new InvalidOperationException(
                "Solo un trabajo en procesamiento puede reprogramarse."
            );
        }

        AiExecutionId = aiExecutionId ?? AiExecutionId;
        ErrorCode = Required(errorCode, "El código de error es requerido.");
        ErrorMessage = Limit(
            Required(errorMessage, "El mensaje de error es requerido."),
            4000
        );
        Status = AiEmailAnalysisJobStatus.RetryScheduled;
        NextAttemptAtUtc = nextAttemptAtUtc > DateTime.UtcNow
            ? nextAttemptAtUtc
            : DateTime.UtcNow;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow);
    }

    public void RecoverExpiredLease(
        string errorCode,
        string errorMessage,
        DateTime nextAttemptAtUtc
    )
    {
        if (Status != AiEmailAnalysisJobStatus.Processing)
        {
            throw new InvalidOperationException(
                "Solo un trabajo en procesamiento puede recuperarse por lease vencido."
            );
        }

        // Perder el lease es un fallo de coordinación del worker, no un intento
        // real fallido contra el proveedor. Se devuelve el contador para que los
        // reintentos de AI sigan reservados para timeout/HTTP/salida inválida.
        AttemptCount = Math.Max(0, AttemptCount - 1);
        ScheduleRetry(errorCode, errorMessage, nextAttemptAtUtc, AiExecutionId);
    }

    public void RequeueAfterLeaseFailure(DateTime nextAttemptAtUtc)
    {
        if (
            Status != AiEmailAnalysisJobStatus.Failed
            || !string.Equals(
                ErrorCode,
                "AI.EmailJobLeaseExpired",
                StringComparison.Ordinal
            )
        )
        {
            throw new InvalidOperationException(
                "Solo un trabajo fallido por pérdida de lease puede reactivarse automáticamente."
            );
        }

        AttemptCount = Math.Max(0, AttemptCount - 1);
        Status = AiEmailAnalysisJobStatus.RetryScheduled;
        NextAttemptAtUtc = nextAttemptAtUtc > DateTime.UtcNow
            ? nextAttemptAtUtc
            : DateTime.UtcNow;
        CompletedAtUtc = null;
        ResultJson = null;
        ReleaseLeaseCore();
        Touch(DateTime.UtcNow);
    }

    public void MarkFailed(
        string errorCode,
        string errorMessage,
        Guid? aiExecutionId = null
    )
    {
        if (Status == AiEmailAnalysisJobStatus.Completed)
        {
            throw new InvalidOperationException(
                "Un trabajo completado no puede marcarse como fallido."
            );
        }

        AiExecutionId = aiExecutionId ?? AiExecutionId;
        ErrorCode = Required(errorCode, "El código de error es requerido.");
        ErrorMessage = Limit(
            Required(errorMessage, "El mensaje de error es requerido."),
            4000
        );
        Status = AiEmailAnalysisJobStatus.Failed;
        CompletedAtUtc = DateTime.UtcNow;
        NextAttemptAtUtc = null;
        ReleaseLeaseCore();
        Touch(CompletedAtUtc.Value);
    }

    private void ReleaseLeaseCore()
    {
        LeaseOwner = null;
        LeaseExpiresAtUtc = null;
        LastHeartbeatAtUtc = null;
    }

    private void Touch(DateTime now)
    {
        Version++;
        MarkAsUpdated(now, null);
    }

    private static string Required(string? value, string errorMessage)
    {
        return string.IsNullOrWhiteSpace(value)
            ? throw new InvalidOperationException(errorMessage)
            : value.Trim();
    }

    private static string Limit(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
