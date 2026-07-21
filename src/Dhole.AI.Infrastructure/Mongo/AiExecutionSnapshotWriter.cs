using System.Text.Json;
using CustomCodeFramework.Mongo.Abstractions;
using Dhole.AI.Application.Abstractions.Mongo;
using Dhole.AI.Infrastructure.Mongo.Documents;

namespace Dhole.AI.Infrastructure.Mongo;

public sealed class AiExecutionSnapshotWriter(IMongoContext mongoContext)
    : IAiExecutionSnapshotWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task WriteAsync(
        Guid executionId,
        string profileKey,
        string executionType,
        string status,
        object? request,
        object? response,
        object? providerMetadata,
        string? errorCode,
        string? errorMessage,
        DateTime occurredAtUtc,
        string? correlationId = null,
        CancellationToken cancellationToken = default
    )
    {
        var document = new AiExecutionSnapshotDocument
        {
            ExecutionId = executionId.ToString(),
            ProfileKey = profileKey,
            ExecutionType = executionType,
            Status = status,

            RequestJson = SerializeNullable(request),

            ResponseJson = SerializeNullable(response),

            ProviderMetadataJson = SerializeNullable(providerMetadata),

            ErrorCode = errorCode,
            ErrorMessage = errorMessage,
            OccurredAtUtc = occurredAtUtc,
            CorrelationId = correlationId,
        };

        return mongoContext
            .GetCollection<AiExecutionSnapshotDocument>()
            .InsertOneAsync(document, cancellationToken: cancellationToken);
    }

    private static string? SerializeNullable(object? value)
    {
        if (value is null)
        {
            return null;
        }

        return value is string text ? text : JsonSerializer.Serialize(value, JsonOptions);
    }
}
