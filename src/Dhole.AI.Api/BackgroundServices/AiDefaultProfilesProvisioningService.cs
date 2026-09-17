using CustomCodeFramework.Persistence.Abstractions;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Application.Abstractions.Repositories;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Models.Entities;
using Dhole.AI.Persistence.Initializations;

namespace Dhole.AI.Api.BackgroundServices;

public sealed class AiDefaultProfilesProvisioningService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AiDefaultProfilesProvisioningService> logger
) : BackgroundService
{
    private const string PricingEmailProfileKey = "pricing-email-analysis";
    private const string PreferredPricingModelId = "qwen3.5:35b-a3b-q4_K_M";

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!ReadBoolean(configuration["AI:DefaultProfiles:Enabled"], true))
        {
            return;
        }

        var retrySeconds = Math.Clamp(
            ReadPositiveInt(configuration["AI:DefaultProfiles:RetrySeconds"], 30),
            10,
            600
        );

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();

                var preferredModelId = await EnsurePreferredPricingModelRegisteredAsync(
                    scope.ServiceProvider,
                    stoppingToken
                );

                var initializer = scope.ServiceProvider
                    .GetRequiredService<AiDefaultProfilesInitializer>();

                var result = await initializer.InitializeAsync(stoppingToken);

                var preferredModelConfigured = preferredModelId.HasValue
                    && await ConfigurePreferredPricingModelAsync(
                        scope.ServiceProvider,
                        preferredModelId.Value,
                        stoppingToken
                    );

                logger.LogInformation(
                    "Provisionamiento de perfiles IA: plantillas creadas {TemplatesCreated}, perfiles creados {ProfilesCreated}, configurados {ProfilesConfigured}, activados {ProfilesActivated}, modelos compatibles {CompatibleModels}, modelo pricing preferido {PreferredPricingModelConfigured}.",
                    result.TemplatesCreated,
                    result.ProfilesCreated,
                    result.ProfilesConfigured,
                    result.ProfilesActivated,
                    result.CompatibleModels,
                    preferredModelConfigured
                );

                if (result.IsReady && preferredModelConfigured)
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "No fue posible provisionar los perfiles predeterminados de IA."
                );
            }

            await Task.Delay(TimeSpan.FromSeconds(retrySeconds), stoppingToken);
        }
    }

    private async Task<Guid?> EnsurePreferredPricingModelRegisteredAsync(
        IServiceProvider services,
        CancellationToken cancellationToken
    )
    {
        var connections = services.GetRequiredService<IAiConnectionRepository>();
        var models = services.GetRequiredService<IAiModelRepository>();
        var providers = services.GetRequiredService<IAiProviderResolver>();
        var secrets = services.GetRequiredService<IAiSecretResolver>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();

        var activeConnections = await connections.GetActiveAsync(cancellationToken);

        foreach (var connection in activeConnections.Where(item =>
            item.ProviderType == AiProviderType.Ollama
        ))
        {
            try
            {
                var secret = await secrets.ResolveAsync(
                    connection.SecretReference,
                    cancellationToken
                );
                var context = new AiProviderContext(
                    connection.Id,
                    connection.Name,
                    connection.ProviderType,
                    connection.BaseUrl,
                    secret,
                    connection.TimeoutSeconds
                );
                var provider = providers.ResolveModelDiscoveryProvider(connection.ProviderType);
                var discovered = await provider.DiscoverAsync(context, cancellationToken);
                var preferred = discovered.FirstOrDefault(item =>
                    string.Equals(
                        item.ExternalModelId,
                        PreferredPricingModelId,
                        StringComparison.OrdinalIgnoreCase
                    )
                );

                if (preferred is null)
                {
                    continue;
                }

                var model = await models.GetByExternalModelIdAsync(
                    connection.Id,
                    PreferredPricingModelId,
                    cancellationToken
                );

                if (model is null)
                {
                    model = AiModel.Create(
                        connection.Id,
                        preferred.ExternalModelId,
                        preferred.Name,
                        preferred.Capabilities,
                        preferred.ContextWindow,
                        preferred.MaximumOutputTokens,
                        null,
                        null,
                        preferred.IsLocal,
                        null
                    );
                    model.MarkAvailable(DateTime.UtcNow);
                    await models.AddAsync(model, cancellationToken);
                }
                else
                {
                    model.Update(
                        connection.Id,
                        preferred.ExternalModelId,
                        preferred.Name,
                        preferred.Capabilities,
                        preferred.ContextWindow ?? model.ContextWindow,
                        preferred.MaximumOutputTokens ?? model.MaximumOutputTokens,
                        model.InputCostPerMillionTokens,
                        model.OutputCostPerMillionTokens,
                        preferred.IsLocal,
                        null
                    );
                    model.Activate(null);
                    model.MarkAvailable(DateTime.UtcNow);
                }

                await unitOfWork.SaveChangesAsync(cancellationToken);

                logger.LogInformation(
                    "Modelo preferido de Pricing {PreferredPricingModel} detectado en Ollama y sincronizado con DholeAI.",
                    PreferredPricingModelId
                );

                return model.Id;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                logger.LogWarning(
                    exception,
                    "No se pudo detectar {PreferredPricingModel} en la conexión Ollama {ConnectionName}.",
                    PreferredPricingModelId,
                    connection.Name
                );
            }
        }

        logger.LogWarning(
            "El modelo preferido de Pricing {PreferredPricingModel} todavía no fue detectado en una conexión Ollama activa.",
            PreferredPricingModelId
        );

        return null;
    }

    private static async Task<bool> ConfigurePreferredPricingModelAsync(
        IServiceProvider services,
        Guid preferredModelId,
        CancellationToken cancellationToken
    )
    {
        var profiles = services.GetRequiredService<IAiProfileRepository>();
        var unitOfWork = services.GetRequiredService<IUnitOfWork>();
        var profile = await profiles.GetByKeyAsync(PricingEmailProfileKey, cancellationToken);

        if (profile is null || profile.IsDeleted)
        {
            return false;
        }

        var current = profile.Models.OrderBy(item => item.Priority).ToArray();
        if (
            current.FirstOrDefault() is { } first
            && first.ModelId == preferredModelId
            && !first.IsFallback
        )
        {
            return true;
        }

        var configurations = new List<(Guid ModelId, int Priority, bool IsFallback)>
        {
            (preferredModelId, 1, false),
        };

        foreach (var item in current.Where(item => item.ModelId != preferredModelId))
        {
            configurations.Add((item.ModelId, configurations.Count + 1, true));
        }

        profile.ConfigureModels(configurations, null);
        if (!profile.IsActive)
        {
            profile.Activate(null);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static bool ReadBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadPositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
