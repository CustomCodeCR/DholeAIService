using System.Text.Json;
using CustomCodeFramework.Core.Domain.Entities;
using CustomCodeFramework.Messaging.Inbox;
using CustomCodeFramework.Messaging.Outbox;
using CustomCodeFramework.Postgres.EntityFramework.Configurations;
using CustomCodeFramework.Postgres.EntityFramework.DbContexts;
using Dhole.AI.Domain.Connections.Entities;
using Dhole.AI.Domain.Executions.Entities;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Domain.Profiles.Entities;
using Dhole.AI.Domain.PromptTemplates.Entities;
using Dhole.AI.Persistence.Auditing;
using Dhole.AI.Persistence.Messaging;
using Microsoft.EntityFrameworkCore;

namespace Dhole.AI.Persistence.DbContexts;

public sealed class ServiceDbContext(DbContextOptions<ServiceDbContext> options)
    : AppDbContextBase(options)
{
    private const string SourceService = "DholeAIService";

    public DbSet<AiConnection> AiConnections => Set<AiConnection>();

    public DbSet<AiModel> AiModels => Set<AiModel>();

    public DbSet<AiProfile> AiProfiles => Set<AiProfile>();

    public DbSet<AiProfileModel> AiProfileModels => Set<AiProfileModel>();

    public DbSet<AiPromptTemplate> AiPromptTemplates => Set<AiPromptTemplate>();

    public DbSet<AiExecution> AiExecutions => Set<AiExecution>();

    public DbSet<AiExecutionAttempt> AiExecutionAttempts => Set<AiExecutionAttempt>();

    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();

    public DbSet<InboxMessage> InboxMessages => Set<InboxMessage>();

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        AddDomainEventsToOutbox();

        return base.SaveChangesAsync(cancellationToken);
    }

    public override int SaveChanges()
    {
        AddDomainEventsToOutbox();

        return base.SaveChanges();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("ai");

        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ServiceDbContext).Assembly);

        modelBuilder.ApplyConfiguration(new OutboxMessageConfiguration());

        modelBuilder.ApplyConfiguration(new InboxMessageConfiguration());
    }

    private void AddDomainEventsToOutbox()
    {
        var aggregateRoots = ChangeTracker
            .Entries()
            .Select(entry => entry.Entity)
            .OfType<AggregateRoot<Guid>>()
            .Where(aggregate => aggregate.DomainEvents.Count > 0)
            .ToList();

        if (aggregateRoots.Count == 0)
        {
            return;
        }

        var outboxMessages = new List<OutboxMessage>();

        foreach (var aggregateRoot in aggregateRoots)
        {
            foreach (var domainEvent in aggregateRoot.DomainEvents)
            {
                var eventType = DomainEventOutboxMapper.GetEventType(domainEvent);

                var eventName = DomainEventOutboxMapper.GetEventName(domainEvent);

                var payloadJson = JsonSerializer.Serialize(domainEvent, domainEvent.GetType());

                var correlationId =
                    AuditExecutionContextAccessor.Current?.CorrelationId ?? Guid.NewGuid();

                outboxMessages.Add(
                    new OutboxMessage
                    {
                        EventId = domainEvent.EventId,
                        EventType = eventType,
                        EventName = eventName,
                        SourceService = SourceService,
                        PayloadJson = payloadJson,
                        HeadersJson = null,
                        CorrelationId = correlationId.ToString(),
                        Status = OutboxMessageStatus.Pending,
                        RetryCount = 0,
                        ErrorMessage = null,
                        CreatedAtUtc = DateTime.UtcNow,
                    }
                );
            }

            aggregateRoot.ClearDomainEvents();
        }

        OutboxMessages.AddRange(outboxMessages);
    }
}
