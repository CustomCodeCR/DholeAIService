using Dhole.AI.Domain.EmailAnalysis.Entities;
using Dhole.AI.Domain.EmailAnalysis.Enums;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class ReusedEmailAnalysisResultTests
{
    private static AiEmailAnalysisJob CreateJob() => AiEmailAnalysisJob.Create(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
        "/api/internal/data-extraction/test", "same-payload-hash", Guid.NewGuid().ToString(), 2);

    [TestMethod]
    public void PendingJob_CompletedFromPriorResultWithoutProviderAttempt()
    {
        var job = CreateJob();
        var executionId = Guid.NewGuid();
        job.CompleteFromPriorResult(executionId, "{\"rows\":[]}");

        Assert.AreEqual(AiEmailAnalysisJobStatus.Completed, job.Status);
        Assert.AreEqual(executionId, job.AiExecutionId);
        Assert.AreEqual(0, job.AttemptCount);
        Assert.IsNotNull(job.CompletedAtUtc);
        Assert.IsNull(job.LeaseOwner);
    }

    [TestMethod]
    public void ReusingResultRejectsMissingExecutionId()
    {
        var job = CreateJob();
        Assert.ThrowsExactly<InvalidOperationException>(
            () => job.CompleteFromPriorResult(Guid.Empty, "{}"));
        Assert.AreEqual(AiEmailAnalysisJobStatus.Pending, job.Status);
    }
}
