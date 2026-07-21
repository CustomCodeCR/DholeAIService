using CustomCodeFramework.Mongo.Abstractions;
using CustomCodeFramework.Mongo.Collections;

namespace Dhole.AI.Infrastructure.Mongo.Documents;

[MongoCollectionName("ai_execution_snapshots")]
public sealed class AiExecutionSnapshotDocument : IReadModel
{
    public string Id { get; init; } = Guid.NewGuid().ToString();

    public string ExecutionId { get; init; } = default!;

    public string ProfileKey { get; init; } = default!;

    public string ExecutionType { get; init; } = default!;

    public string Status { get; init; } = default!;

    public string? RequestJson { get; init; }

    public string? ResponseJson { get; init; }

    public string? ProviderMetadataJson { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTime OccurredAtUtc { get; init; }

    public string? CorrelationId { get; init; }

    public string SourceService { get; init; } = "DholeAIService";
}
