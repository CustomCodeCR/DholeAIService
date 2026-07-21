using CustomCodeFramework.Mongo.Abstractions;
using CustomCodeFramework.Mongo.Collections;

namespace Dhole.AI.Infrastructure.Mongo.Documents;

[MongoCollectionName("ai_configuration_snapshots")]
public sealed class AiConfigurationSnapshotDocument : IReadModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public string EventId { get; init; } = default!;

    public string EventName { get; init; } = default!;

    public string EntityType { get; init; } = default!;

    public string EntityId { get; init; } = default!;

    public string Action { get; init; } = default!;

    public string? PreviousValueJson { get; init; }

    public string? NewValueJson { get; init; }

    public string? ChangedBy { get; init; }

    public DateTime ChangedAtUtc { get; init; }

    public string? CorrelationId { get; init; }

    public string SourceService { get; init; } = "DholeAIService";
}
