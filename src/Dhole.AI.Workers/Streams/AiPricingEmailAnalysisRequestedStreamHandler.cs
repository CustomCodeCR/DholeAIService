using System.Text.Json;
using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Abstractions.Messaging;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.EmailAnalysis.Entities;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Worker.EmailAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Worker.Streams;

internal sealed class AiPricingEmailAnalysisRequestedStreamHandler(
    ServiceDbContext dbContext,
    IAiAuditService audit,
    IIntegrationEventOutboxWriter outbox,
    IConfiguration configuration,
    ILogger<AiPricingEmailAnalysisRequestedStreamHandler> logger
) : IRedisStreamMessageHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    public string MessageType => EmailAnalysisMessageTypes.Requested;

    public async Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    )
    {
        var integrationEvent =
            AiEmailStreamPayloadReader.Read<AiPricingEmailAnalysisRequestedIntegrationEvent>(
                envelope
            );
        Validate(integrationEvent);

        var exists = await dbContext.AiEmailAnalysisJobs.AnyAsync(
            item => item.ExternalRequestId == integrationEvent.RequestId,
            cancellationToken
        );
        if (exists)
        {
            return;
        }

        // DataExtraction can legitimately replay the same logical payload after recovering
        // coordination state. If the original AI job already completed, do not merely drop
        // the new RequestId: replay the completed business result using the caller's current
        // RequestId so DataExtraction can close its active job instead of waiting forever.
        var duplicateJob = await dbContext.AiEmailAnalysisJobs
            .Where(item =>
                item.EmailMessageId == integrationEvent.EmailMessageId
                && item.EmailAttachmentId == integrationEvent.EmailAttachmentId
                && item.RequestHash == integrationEvent.RequestHash
                && item.Status != AiEmailAnalysisJobStatus.Failed)
            .OrderByDescending(item => item.CreatedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (duplicateJob is not null)
        {
            if (
                duplicateJob.Status == AiEmailAnalysisJobStatus.Completed
                && duplicateJob.AiExecutionId.HasValue
                && !string.IsNullOrWhiteSpace(duplicateJob.ResultJson)
            )
            {
                var parsed = JsonSerializer.Deserialize<ParsedAiPricingEmailResult>(
                    duplicateJob.ResultJson,
                    JsonOptions
                ) ?? throw new InvalidOperationException(
                    "El resultado persistido del análisis AI duplicado no pudo deserializarse."
                );

                var completedEvent = new AiPricingEmailAnalysisCompletedIntegrationEvent(
                    Guid.NewGuid(),
                    integrationEvent.RequestId,
                    integrationEvent.EmailExtractionJobId,
                    duplicateJob.Id,
                    duplicateJob.AiExecutionId.Value,
                    integrationEvent.CorrelationId,
                    integrationEvent.RequestHash,
                    parsed.Confidence,
                    parsed.Rows,
                    parsed.Warnings,
                    DateTime.UtcNow
                );

                await outbox.WriteAsync(
                    typeof(AiPricingEmailAnalysisCompletedIntegrationEvent).FullName!,
                    EmailAnalysisMessageTypes.Completed,
                    completedEvent,
                    integrationEvent.CorrelationId,
                    cancellationToken
                );
                await dbContext.SaveChangesAsync(cancellationToken);

                logger.LogWarning(
                    "Solicitud AI duplicada {RequestId} reutilizó resultado completado del AI job {AiJobId}; "
                        + "correo {EmailMessageId}; RequestHash {RequestHash}.",
                    integrationEvent.RequestId,
                    duplicateJob.Id,
                    integrationEvent.EmailMessageId,
                    integrationEvent.RequestHash
                );
                return;
            }

            logger.LogInformation(
                "Solicitud AI duplicada ignorada porque el trabajo original sigue activo. "
                    + "solicitud {RequestId}; AI job {AiJobId}; estado {Status}; correo {EmailMessageId}; "
                    + "adjunto {EmailAttachmentId}; RequestHash {RequestHash}; CorrelationId {CorrelationId}.",
                integrationEvent.RequestId,
                duplicateJob.Id,
                duplicateJob.Status,
                integrationEvent.EmailMessageId,
                integrationEvent.EmailAttachmentId,
                integrationEvent.RequestHash,
                integrationEvent.CorrelationId
            );
            return;
        }

        var job = AiEmailAnalysisJob.Create(
            integrationEvent.RequestId,
            integrationEvent.EmailExtractionJobId,
            integrationEvent.EmailMessageId,
            integrationEvent.EmailAttachmentId,
            integrationEvent.PayloadUrl,
            integrationEvent.RequestHash,
            integrationEvent.CorrelationId,
            ReadPositiveInt(configuration["AI:EmailJobs:MaxRetryCount"], 3)
        );
        await dbContext.AiEmailAnalysisJobs.AddAsync(job, cancellationToken);

        await audit.PublishAsync(
            new AiAuditEvent(
                EventType: AiAuditEventTypes.EmailAnalysisInputRecorded,
                Action: AiAuditActions.InputRecorded,
                EntityType: AiAuditEntityTypes.EmailAnalysisJob,
                EntityId: job.Id,
                Payload: new
                {
                    Stage = "email-request-received",
                    Job = new
                    {
                        job.Id,
                        job.ExternalRequestId,
                        job.EmailExtractionJobId,
                        job.EmailMessageId,
                        job.EmailAttachmentId,
                        job.PayloadUrl,
                        job.RequestHash,
                        job.CorrelationId,
                        job.MaxAttemptCount,
                    },
                    IntegrationEvent = integrationEvent,
                    Envelope = new
                    {
                        envelope.MessageId,
                        envelope.MessageType,
                        envelope.PayloadJson,
                    },
                },
                Metadata: new
                {
                    Stage = "email-request-received",
                    Source = "RedisStream",
                },
                CorrelationId: integrationEvent.CorrelationId
            ),
            cancellationToken
        );
        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            dbContext.ChangeTracker.Clear();
            if (
                await dbContext.AiEmailAnalysisJobs.AnyAsync(
                    item => item.ExternalRequestId == integrationEvent.RequestId,
                    cancellationToken
                )
            )
            {
                logger.LogDebug(
                    "La solicitud AI {RequestId} ya fue persistida por otro consumidor.",
                    integrationEvent.RequestId
                );
                return;
            }

            throw;
        }

        logger.LogInformation(
            "Solicitud de correo persistida para AI. AI job {AiJobId}; "
                + "solicitud {RequestId}; trabajo {EmailExtractionJobId}; "
                + "correo {EmailMessageId}; CorrelationId {CorrelationId}; "
                + "RequestHash {RequestHash}.",
            job.Id,
            job.ExternalRequestId,
            job.EmailExtractionJobId,
            job.EmailMessageId,
            job.CorrelationId,
            job.RequestHash
        );
    }

    private static void Validate(
        AiPricingEmailAnalysisRequestedIntegrationEvent integrationEvent
    )
    {
        if (
            integrationEvent.RequestId == Guid.Empty
            || integrationEvent.EmailExtractionJobId == Guid.Empty
            || integrationEvent.EmailMessageId == Guid.Empty
            || string.IsNullOrWhiteSpace(integrationEvent.PayloadUrl)
            || string.IsNullOrWhiteSpace(integrationEvent.RequestHash)
            || string.IsNullOrWhiteSpace(integrationEvent.CorrelationId)
        )
        {
            throw new InvalidOperationException(
                "La solicitud de análisis de correo AI está incompleta."
            );
        }
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? parsed
            : fallback;
    }
}
