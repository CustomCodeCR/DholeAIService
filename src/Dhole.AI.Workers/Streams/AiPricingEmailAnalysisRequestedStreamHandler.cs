using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;
using Dhole.AI.Application.Abstractions.Auditing;
using Dhole.AI.Application.Auditing;
using Dhole.AI.Domain.EmailAnalysis.Entities;
using Dhole.AI.Persistence.DbContexts;
using Dhole.AI.Worker.EmailAnalysis;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Worker.Streams;

internal sealed class AiPricingEmailAnalysisRequestedStreamHandler(
    ServiceDbContext dbContext,
    IAiAuditService audit,
    IConfiguration configuration,
    ILogger<AiPricingEmailAnalysisRequestedStreamHandler> logger
) : IRedisStreamMessageHandler
{
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
                    item =>
                        item.ExternalRequestId
                        == integrationEvent.RequestId,
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
