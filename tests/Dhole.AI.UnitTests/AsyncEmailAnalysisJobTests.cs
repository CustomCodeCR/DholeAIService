using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Domain.EmailAnalysis.Entities;
using Dhole.AI.Domain.EmailAnalysis.Enums;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class AsyncEmailAnalysisJobTests
{
    [TestMethod]
    public void Job_RetriesTransientFailuresAndStopsAtConfiguredAttemptCount()
    {
        var job = CreateJob(maxAttemptCount: 3);

        job.MarkProcessing("worker-a", DateTime.UtcNow.AddMinutes(30));
        job.ScheduleRetry(
            "AI.ProviderTimeout",
            "timeout",
            DateTime.UtcNow.AddSeconds(30)
        );
        job.MarkProcessing("worker-b", DateTime.UtcNow.AddMinutes(30));
        job.ScheduleRetry(
            "AI.ProviderTimeout",
            "timeout",
            DateTime.UtcNow.AddSeconds(30)
        );
        job.MarkProcessing("worker-c", DateTime.UtcNow.AddMinutes(30));
        job.MarkFailed("AI.ProviderTimeout", "timeout");

        Assert.AreEqual(AiEmailAnalysisJobStatus.Failed, job.Status);
        Assert.AreEqual(3, job.AttemptCount);
        Assert.AreEqual(3, job.MaxAttemptCount);
        Assert.IsNull(job.LeaseOwner);
        Assert.IsNotNull(job.CompletedAtUtc);
    }

    [TestMethod]
    public void Job_CompletionIsIdempotent()
    {
        var job = CreateJob(maxAttemptCount: 3);
        var executionId = Guid.NewGuid();

        job.MarkProcessing("worker-a", DateTime.UtcNow.AddMinutes(30));
        job.MarkCompleted(executionId, """{"confidence":95}""");
        var versionAfterFirstCompletion = job.Version;
        job.MarkCompleted(executionId, """{"confidence":95}""");

        Assert.AreEqual(AiEmailAnalysisJobStatus.Completed, job.Status);
        Assert.AreEqual(executionId, job.AiExecutionId);
        Assert.AreEqual(versionAfterFirstCompletion, job.Version);
    }

    [TestMethod]
    public void Worker_UsesApplicationOrchestratorAndHasNoSelfGrpcDependency()
    {
        var workerType = typeof(Dhole.AI.Workers.Worker)
            .Assembly.GetType(
                "Dhole.AI.Worker.Workers.AiEmailAnalysisWorker",
                throwOnError: true
            )!;
        var dependencyTypes = workerType
            .GetConstructors(
                System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic
            )
            .Single()
            .GetParameters()
            .Select(parameter => parameter.ParameterType)
            .ToArray();

        CollectionAssert.Contains(
            dependencyTypes,
            typeof(IAiExecutionOrchestrator)
        );
        Assert.IsFalse(
            dependencyTypes.Any(type =>
                type.FullName?.Contains(
                    "Grpc",
                    StringComparison.OrdinalIgnoreCase
                ) == true
            ),
            "The AI email worker must call Application directly, not its own gRPC API."
        );
    }

    private static AiEmailAnalysisJob CreateJob(int maxAttemptCount)
    {
        return AiEmailAnalysisJob.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            "http://data-extraction/api/internal/request",
            "request-hash",
            "correlation-id",
            maxAttemptCount
        );
    }
}
