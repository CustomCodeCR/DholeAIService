using CustomCodeFramework.Messaging.DependencyInjection;
using CustomCodeFramework.Messaging.Outbox.DependencyInjection;
using CustomCodeFramework.Redis.Streams.DependencyInjection;
using CustomCodeFramework.Workers.DependencyInjection;
using Dhole.AI.Infrastructure.DependencyInjection;
using Dhole.AI.Worker.Outbox;
using Dhole.AI.Worker.Streams;
using Dhole.AI.Worker.Workers;

namespace Dhole.AI.Worker.DependencyInjection;

public static class WorkerServiceCollectionExtensions
{
    public static IServiceCollection AddAiWorker(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddAiWorkerInfrastructure(configuration);
        services.AddCustomCodeRedisStreams(configuration);

        services.AddAiMessaging(configuration);
        services.AddAiStreamHandlers();
        services.AddAiPeriodicWorkers(configuration);

        return services;
    }

    private static IServiceCollection AddAiMessaging(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeMessaging(configuration);
        services.AddCustomCodeMessagingOutbox(configuration);

        services.AddCustomCodeOutboxProcessor<OutboxProcessor>();
        services.AddCustomCodeInboxProcessor<InboxProcessor>();

        services.AddCustomCodeMessagingOutboxHostedServices();
        services.AddCustomCodeRedisStreamConsumerBackgroundService();

        return services;
    }

    private static IServiceCollection AddAiStreamHandlers(this IServiceCollection services)
    {
        services.AddCustomCodeRedisStreamHandler<AiConnectionCreatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiConnectionUpdatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiConnectionDeletedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiConnectionActivatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiConnectionInactivatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<AiModelCreatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiModelUpdatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiModelDeletedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiModelActivatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiModelInactivatedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<AiProfileCreatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiProfileUpdatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiProfileDeletedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiProfileActivatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiProfileInactivatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiProfileModelsChangedStreamHandler>();

        services.AddCustomCodeRedisStreamHandler<AiPromptTemplateCreatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiPromptTemplateUpdatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiPromptTemplateDeletedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiPromptTemplateActivatedStreamHandler>();
        services.AddCustomCodeRedisStreamHandler<AiPromptTemplateInactivatedStreamHandler>();

        return services;
    }

    private static IServiceCollection AddAiPeriodicWorkers(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeWorkers(configuration);

        services.AddCustomCodePeriodicWorker<AiCacheWarmupWorker>();
        services.AddCustomCodePeriodicWorker<AiConnectionHealthWorker>();
        services.AddCustomCodePeriodicWorker<AiExecutionCleanupWorker>();

        return services;
    }
}
