using System.Text.Json;
using CustomCodeFramework.Mongo.Abstractions;
using Dhole.AI.Application.Abstractions.Mongo;
using Dhole.AI.Infrastructure.Mongo.Documents;

namespace Dhole.AI.Infrastructure.Mongo;

public sealed class AiConfigurationSnapshotWriter(IMongoContext mongoContext)
    : IAiConfigurationSnapshotWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public Task WriteAsync(
        Guid eventId,
        string eventName,
        string entityType,
        Guid entityId,
        string action,
        object? previousValue,
        object? newValue,
        Guid? changedBy,
        DateTime changedAtUtc,
        Guid? correlationId = null,
        CancellationToken cancellationToken = default
    )
    {
        var document = new AiConfigurationSnapshotDocument
        {
            EventId = eventId.ToString(),
            EventName = eventName,
            EntityType = entityType,
            EntityId = entityId.ToString(),
            Action = action,

            PreviousValueJson = SerializeNullable(previousValue),

            NewValueJson = SerializeNullable(newValue),

            ChangedBy = changedBy?.ToString(),
            ChangedAtUtc = changedAtUtc,

            CorrelationId = correlationId?.ToString(),
        };

        return mongoContext
            .GetCollection<AiConfigurationSnapshotDocument>()
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
