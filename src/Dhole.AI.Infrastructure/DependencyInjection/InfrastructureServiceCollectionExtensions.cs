using CustomCodeFramework.Auth.DependencyInjection;
using CustomCodeFramework.Core.Abstractions;
using CustomCodeFramework.Mongo.DependencyInjection;
using CustomCodeFramework.Redis.DependencyInjection;
using Dhole.AI.Application.Abstractions.Cache;
using Dhole.AI.Application.Abstractions.Mongo;
using Dhole.AI.Application.Abstractions.Providers;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Infrastructure.Cache;
using Dhole.AI.Infrastructure.Mongo;
using Dhole.AI.Infrastructure.Providers;
using Dhole.AI.Infrastructure.Providers.Common;
using Dhole.AI.Infrastructure.Providers.Gemini;
using Dhole.AI.Infrastructure.Providers.Ollama;
using Dhole.AI.Infrastructure.Providers.OpenAI;
using Dhole.AI.Infrastructure.Providers.OpenAICompatible;
using Dhole.AI.Infrastructure.Secrets;
using Dhole.AI.Infrastructure.Time;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.AI.Infrastructure.DependencyInjection;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeAuth(configuration);

        services.PostConfigure<AuthenticationOptions>(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;

            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;

            options.DefaultScheme = JwtBearerDefaults.AuthenticationScheme;
        });

        return services.AddAiCoreInfrastructure(configuration);
    }

    public static IServiceCollection AddAiWorkerInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        return services.AddAiCoreInfrastructure(configuration);
    }

    private static IServiceCollection AddAiCoreInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration
    )
    {
        services.AddCustomCodeRedis(configuration);
        services.AddCustomCodeMongo(configuration);

        services.AddAiCacheServices();
        services.AddAiSnapshotWriters();
        services.AddAiSecretServices();
        services.AddAiHttpClients();
        services.AddAiProviders();

        services.AddSingleton<IDateTimeProvider, SystemDateTimeProvider>();

        return services;
    }

    private static IServiceCollection AddAiCacheServices(this IServiceCollection services)
    {
        services.AddScoped<IAiConnectionCacheService, AiConnectionCacheService>();

        services.AddScoped<IAiModelCacheService, AiModelCacheService>();

        services.AddScoped<IAiProfileCacheService, AiProfileCacheService>();

        services.AddScoped<IAiPromptTemplateCacheService, AiPromptTemplateCacheService>();

        return services;
    }

    private static IServiceCollection AddAiSnapshotWriters(this IServiceCollection services)
    {
        services.AddScoped<IAiConfigurationSnapshotWriter, AiConfigurationSnapshotWriter>();

        services.AddScoped<IAiExecutionSnapshotWriter, AiExecutionSnapshotWriter>();

        return services;
    }

    private static IServiceCollection AddAiSecretServices(this IServiceCollection services)
    {
        services.AddSingleton<IAiSecretSource, EnvironmentSecretResolver>();

        services.AddSingleton<IAiSecretResolver, SecretResolver>();

        return services;
    }

    private static IServiceCollection AddAiHttpClients(this IServiceCollection services)
    {
        AddClient(services, AiHttpClientNames.OpenAI);

        AddClient(services, AiHttpClientNames.Gemini);

        AddClient(services, AiHttpClientNames.Ollama);

        AddClient(services, AiHttpClientNames.OpenAICompatible);

        return services;
    }

    private static void AddClient(IServiceCollection services, string name)
    {
        services
            .AddHttpClient(
                name,
                client =>
                {
                    /*
                     * El timeout se controla por conexión mediante
                     * CancellationTokenSource. Se desactiva aquí para
                     * evitar dos timeouts compitiendo.
                     */
                    client.Timeout = Timeout.InfiniteTimeSpan;
                }
            )
            .ConfigurePrimaryHttpMessageHandler(() =>
                new SocketsHttpHandler
                {
                    PooledConnectionLifetime = TimeSpan.FromMinutes(10),

                    PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),

                    EnableMultipleHttp2Connections = true,

                    AutomaticDecompression =
                        System.Net.DecompressionMethods.GZip
                        | System.Net.DecompressionMethods.Deflate,
                }
            );
    }

    private static IServiceCollection AddAiProviders(this IServiceCollection services)
    {
        services.AddScoped<IAiChatProvider, OpenAiChatProvider>();

        services.AddScoped<IAiEmbeddingProvider, OpenAiEmbeddingProvider>();

        services.AddScoped<IAiModelDiscoveryProvider, OpenAiModelDiscoveryProvider>();

        services.AddScoped<IAiProviderHealthChecker, OpenAiHealthChecker>();

        services.AddScoped<IAiChatProvider, GeminiChatProvider>();

        services.AddScoped<IAiEmbeddingProvider, GeminiEmbeddingProvider>();

        services.AddScoped<IAiModelDiscoveryProvider, GeminiModelDiscoveryProvider>();

        services.AddScoped<IAiProviderHealthChecker, GeminiHealthChecker>();

        services.AddScoped<IAiChatProvider, OllamaChatProvider>();

        services.AddScoped<IAiEmbeddingProvider, OllamaEmbeddingProvider>();

        services.AddScoped<IAiModelDiscoveryProvider, OllamaModelDiscoveryProvider>();

        services.AddScoped<IAiProviderHealthChecker, OllamaHealthChecker>();

        services.AddScoped<IAiChatProvider, OpenAiCompatibleChatProvider>();

        services.AddScoped<IAiEmbeddingProvider, OpenAiCompatibleEmbeddingProvider>();

        services.AddScoped<IAiModelDiscoveryProvider, OpenAiCompatibleModelDiscoveryProvider>();

        services.AddScoped<IAiProviderHealthChecker, OpenAiCompatibleHealthChecker>();

        services.AddScoped<IAiProviderResolver, AiProviderResolver>();

        return services;
    }
}
