using CustomCodeFramework.Redis.Streams.Abstractions;
using CustomCodeFramework.Redis.Streams.Messages;

namespace Dhole.AI.Worker.Streams;

/// <summary>
/// The AI service publishes execution lifecycle domain events to dhole.ai.events for
/// observability/auditing. The AI worker does not need to react to its own lifecycle
/// events, but registering no handler leaves them in the consumer group's PEL in
/// deployments where unsupported messages are not acknowledged by the stream framework.
/// These no-op handlers deliberately consume them so they can be acknowledged.
/// </summary>
internal abstract class AiExecutionLifecycleIgnoredStreamHandler : IRedisStreamMessageHandler
{
    protected AiExecutionLifecycleIgnoredStreamHandler(string messageType)
    {
        MessageType = messageType;
    }

    public string MessageType { get; }

    public Task HandleAsync(
        RedisStreamEnvelope envelope,
        CancellationToken cancellationToken = default
    ) => Task.CompletedTask;
}

internal sealed class IgnoreAiExecutionStartedStreamHandler
    : AiExecutionLifecycleIgnoredStreamHandler
{
    public IgnoreAiExecutionStartedStreamHandler()
        : base("ai.execution.started") { }
}

internal sealed class IgnoreAiExecutionCompletedStreamHandler
    : AiExecutionLifecycleIgnoredStreamHandler
{
    public IgnoreAiExecutionCompletedStreamHandler()
        : base("ai.execution.completed") { }
}

internal sealed class IgnoreAiExecutionFailedStreamHandler
    : AiExecutionLifecycleIgnoredStreamHandler
{
    public IgnoreAiExecutionFailedStreamHandler()
        : base("ai.execution.failed") { }
}
