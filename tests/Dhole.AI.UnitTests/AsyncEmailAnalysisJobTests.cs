using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Domain.EmailAnalysis;
using Dhole.AI.Domain.EmailAnalysis.Entities;
using Dhole.AI.Domain.Executions.Enums;
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
    public void LeaseRecovery_DoesNotConsumeProviderAttempt()
    {
        var job = CreateJob(maxAttemptCount: 3);

        job.MarkProcessing("worker-a", DateTime.UtcNow.AddMinutes(30));
        job.RecoverExpiredLease(
            "AI.EmailJobLeaseExpired",
            "lease expired",
            DateTime.UtcNow
        );

        Assert.AreEqual(AiEmailAnalysisJobStatus.RetryScheduled, job.Status);
        Assert.AreEqual(0, job.AttemptCount);
        Assert.AreEqual(3, job.MaxAttemptCount);
        Assert.IsNull(job.LeaseOwner);
        Assert.IsNotNull(job.NextAttemptAtUtc);
    }

    [TestMethod]
    public void FailedLeaseJob_CanBeReactivatedAfterDeployment()
    {
        var job = CreateJob(maxAttemptCount: 3);

        job.MarkProcessing("worker-a", DateTime.UtcNow.AddMinutes(30));
        job.MarkFailed(
            "AI.EmailJobLeaseExpired",
            "El trabajo AI agotó sus intentos después de perder el lease."
        );
        job.RequeueAfterLeaseFailure(DateTime.UtcNow);

        Assert.AreEqual(AiEmailAnalysisJobStatus.RetryScheduled, job.Status);
        Assert.AreEqual(0, job.AttemptCount);
        Assert.IsNull(job.CompletedAtUtc);
        Assert.IsNull(job.LeaseOwner);
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
    public void DeletedSource_CancelsQueuedJobAndKeepsAuditMetadata()
    {
        var job = CreateJob(maxAttemptCount: 2);

        job.CancelUnavailableSource("AI.SourceUnavailable", "Original eliminado.");

        Assert.AreEqual(AiEmailAnalysisJobStatus.Cancelled, job.Status);
        Assert.AreEqual("AI.SourceUnavailable", job.ErrorCode);
        Assert.AreEqual("Original eliminado.", job.ErrorMessage);
        Assert.AreEqual(0, job.AttemptCount);
        Assert.IsNotNull(job.CompletedAtUtc);
        Assert.IsNull(job.NextAttemptAtUtc);
        var version = job.Version;

        job.CancelUnavailableSource("AI.SourceUnavailable", "Duplicado.");

        Assert.AreEqual(version, job.Version);
        Assert.AreEqual("Original eliminado.", job.ErrorMessage);
    }

    [TestMethod]
    public void HistoricalSource_CancelsInFlightButNeverOverwritesCompletedWork()
    {
        var inFlight = CreateJob(maxAttemptCount: 2);
        inFlight.MarkProcessing("worker-a", DateTime.UtcNow.AddMinutes(10));
        inFlight.CancelHistoricalMonth();
        Assert.AreEqual(AiEmailAnalysisJobStatus.Cancelled, inFlight.Status);
        Assert.IsNull(inFlight.LeaseOwner);
        Assert.AreEqual("AI.HistoricalMonthClosed", inFlight.ErrorCode);

        var completed = CreateJob(maxAttemptCount: 2);
        completed.MarkProcessing("worker-a", DateTime.UtcNow.AddMinutes(10));
        completed.MarkCompleted(Guid.NewGuid(), """{"confidence":99}""");
        completed.CancelHistoricalMonth();
        Assert.AreEqual(AiEmailAnalysisJobStatus.Completed, completed.Status);
    }

    [TestMethod]
    public void CompletedInferenceWithFailedHandoff_IsDisplayedAsReviewNotModelFailure()
    {
        Assert.AreEqual(
            AiEmailAnalysisPresentation.CompletedWithReview,
            AiEmailAnalysisPresentation.Resolve(
                AiEmailAnalysisJobStatus.Failed,
                AiExecutionStatus.Completed));

        Assert.AreEqual(
            AiEmailAnalysisJobStatus.Failed.ToString(),
            AiEmailAnalysisPresentation.Resolve(
                AiEmailAnalysisJobStatus.Failed,
                AiExecutionStatus.Failed));

        Assert.AreEqual(
            AiEmailAnalysisJobStatus.Completed.ToString(),
            AiEmailAnalysisPresentation.Resolve(
                AiEmailAnalysisJobStatus.Completed,
                AiExecutionStatus.Completed));
    }

    [TestMethod]
    public void HistoricalFailureWithNoExecution_RemainsAnActualFailure()
    {
        Assert.AreEqual(
            AiEmailAnalysisJobStatus.Failed.ToString(),
            AiEmailAnalysisPresentation.Resolve(
                AiEmailAnalysisJobStatus.Failed,
                null));
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
