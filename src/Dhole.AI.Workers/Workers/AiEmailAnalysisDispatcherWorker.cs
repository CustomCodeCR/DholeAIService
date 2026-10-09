using CustomCodeFramework.Workers.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Dhole.AI.Worker.Workers;

/// <summary>
/// Runs a small number of isolated AiEmailAnalysisWorker instances in parallel.
/// AiEmailAnalysisWorker intentionally owns a scoped DbContext, so running several
/// jobs on one instance would be unsafe. Each slot receives its own DI scope and its
/// own DbContext while PostgreSQL FOR UPDATE SKIP LOCKED guarantees that two slots do
/// not claim the same job.
/// </summary>
internal sealed class AiEmailAnalysisDispatcherWorker(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<AiEmailAnalysisDispatcherWorker> logger
) : IBackgroundWorker
{
    private const int MaximumConfiguredParallelism = 8;
    private static readonly SemaphoreSlim DispatchGate = new(1, 1);

    public string Name => "ai.email-analysis-dispatcher";

    public async Task ExecuteAsync(
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        if (!ReadBoolean(configuration["AI:EmailJobs:Enabled"], true))
        {
            return;
        }

        var configuredParallelism = ReadPositiveInt(
            configuration["AI:EmailJobs:MaxConcurrentJobs"],
            1
        );
        var parallelism = Math.Clamp(
            configuredParallelism,
            1,
            MaximumConfiguredParallelism
        );

        // The scheduler can tick again while a long-running model request is active.
        // Keep the in-process job budget bounded even if periodic executions overlap.
        if (!await DispatchGate.WaitAsync(0, cancellationToken))
        {
            logger.LogDebug("AI email dispatcher is already running; skipping overlapping cycle.");
            return;
        }

        try
        {
            // Maintenance touches global job state. Run it only once, not concurrently
            // on each isolated processing slot.
            await using (var maintenanceScope = scopeFactory.CreateAsyncScope())
            {
                var maintenanceWorker =
                    maintenanceScope.ServiceProvider.GetRequiredService<AiEmailAnalysisWorker>();
                await maintenanceWorker.PrepareAsync(cancellationToken);
            }

            logger.LogInformation("Dispatching AI email jobs with {Parallelism} parallel slots.", parallelism);
            var tasks = Enumerable.Range(0, parallelism)
                .Select(slot => RunSlotAsync(slot, context, cancellationToken))
                .ToArray();

            await Task.WhenAll(tasks);
        }
        finally
        {
            DispatchGate.Release();
        }
    }

    private async Task RunSlotAsync(
        int slot,
        IWorkerExecutionContext context,
        CancellationToken cancellationToken
    )
    {
        try
        {
            // Stagger the recovery/claim phase slightly so the first slot can finish
            // its fast coordination work before the next isolated worker starts.
            if (slot > 0)
            {
                await Task.Delay(
                    TimeSpan.FromMilliseconds(500 * slot),
                    cancellationToken
                );
            }

            await using var scope = scopeFactory.CreateAsyncScope();
            var worker = scope.ServiceProvider.GetRequiredService<AiEmailAnalysisWorker>();
            await worker.ProcessAvailableJobsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            // One provider slot failing must not prevent the other slot from draining
            // its job. The periodic dispatcher will try again on the next cycle.
            logger.LogError(
                exception,
                "Falló el slot paralelo {Slot} de análisis AI de correos.",
                slot + 1
            );
        }
    }

    private static bool ReadBoolean(string? value, bool fallback)
    {
        return bool.TryParse(value, out var parsed) ? parsed : fallback;
    }

    private static int ReadPositiveInt(string? value, int fallback)
    {
        return int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;
    }
}
