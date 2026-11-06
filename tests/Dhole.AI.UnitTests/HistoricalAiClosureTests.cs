using Dhole.AI.Domain.EmailAnalysis.Entities;
using Dhole.AI.Domain.EmailAnalysis.Enums;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class HistoricalAiClosureTests
{
    private static AiEmailAnalysisJob Create() => AiEmailAnalysisJob.Create(
        Guid.NewGuid(),
        Guid.NewGuid(),
        Guid.NewGuid(),
        null,
        "/api/internal/data-extraction/ai-email-requests/123",
        "hash",
        "correlation",
        2);

    [TestMethod]
    public void PendingHistoricalJob_ClosesWithoutProviderAttempt()
    {
        var job = Create();
        job.CancelHistoricalMonth();
        Assert.AreEqual(AiEmailAnalysisJobStatus.Cancelled, job.Status);
        Assert.AreEqual("AI.HistoricalMonthClosed", job.ErrorCode);
        Assert.AreEqual(0, job.AttemptCount);
        Assert.IsNull(job.NextAttemptAtUtc);
        Assert.IsNotNull(job.CompletedAtUtc);
        job.CancelHistoricalMonth();
        Assert.AreEqual(AiEmailAnalysisJobStatus.Cancelled, job.Status);
    }

    [TestMethod]
    public void CompletedHistoricalJob_RemainsSuccessful()
    {
        var job = Create();
        job.MarkProcessing("test", DateTime.UtcNow.AddMinutes(5));
        job.MarkCompleted(Guid.NewGuid(), "{\"rows\":[]}");
        job.CancelHistoricalMonth();
        Assert.AreEqual(AiEmailAnalysisJobStatus.Completed, job.Status);
    }
}
