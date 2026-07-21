namespace Dhole.AI.Application.Abstractions.Auditing;

public interface IAiAuditService
{
    Task PublishAsync(AiAuditEvent auditEvent, CancellationToken cancellationToken = default);
}
