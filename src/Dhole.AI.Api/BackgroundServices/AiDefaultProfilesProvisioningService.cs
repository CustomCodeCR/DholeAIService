using Dhole.AI.Persistence.Initializations;

namespace Dhole.AI.Api.BackgroundServices;

public sealed class AiDefaultProfilesProvisioningService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AiDefaultProfilesProvisioningService> logger
) : BackgroundService
{
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
                var initializer = scope.ServiceProvider
                    .GetRequiredService<AiDefaultProfilesInitializer>();

                var result = await initializer.InitializeAsync(stoppingToken);

                logger.LogInformation(
                    "Provisionamiento de perfiles IA: plantillas creadas {TemplatesCreated}, perfiles creados {ProfilesCreated}, configurados {ProfilesConfigured}, activados {ProfilesActivated}, modelos compatibles {CompatibleModels}.",
                    result.TemplatesCreated,
                    result.ProfilesCreated,
                    result.ProfilesConfigured,
                    result.ProfilesActivated,
                    result.CompatibleModels
                );

                if (result.IsReady)
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

    private static bool ReadBoolean(string? value, bool fallback) =>
        bool.TryParse(value, out var parsed) ? parsed : fallback;

    private static int ReadPositiveInt(string? value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
}
