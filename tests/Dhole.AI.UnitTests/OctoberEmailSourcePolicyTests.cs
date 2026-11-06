using Dhole.AI.Domain.EmailAnalysis.Entities;
using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Worker.Workers;

namespace Dhole.AI.UnitTests;

[TestClass]
public sealed class OctoberEmailSourcePolicyTests
{
    [TestMethod]
    public void OctoberSourceReceivedInOctober_RemainsOctoberEvenIfAiEnqueuedInNovember()
    {
        var originalReceipt = new DateTime(2026, 10, 15, 17, 0, 0, DateTimeKind.Utc);
        var enqueuedInNovember = new DateTime(2026, 11, 6, 15, 0, 0, DateTimeKind.Utc);

        Assert.IsTrue(OctoberEmailSourcePolicy.ShouldClose(originalReceipt));
        Assert.IsTrue(enqueuedInNovember > originalReceipt);
    }

    [TestMethod]
    public void OctoberBoundary_UsesCostaRicaTimezoneNotUtcCalendarMonth()
    {
        Assert.IsFalse(OctoberEmailSourcePolicy.ShouldClose(
            new DateTime(2026, 10, 1, 5, 59, 59, DateTimeKind.Utc)));
        Assert.IsTrue(OctoberEmailSourcePolicy.ShouldClose(
            new DateTime(2026, 10, 1, 6, 0, 0, DateTimeKind.Utc)));
        Assert.IsTrue(OctoberEmailSourcePolicy.ShouldClose(
            new DateTime(2026, 11, 1, 5, 59, 59, DateTimeKind.Utc)));
        Assert.IsFalse(OctoberEmailSourcePolicy.ShouldClose(
            new DateTime(2026, 11, 1, 6, 0, 0, DateTimeKind.Utc)));
        Assert.IsFalse(OctoberEmailSourcePolicy.ShouldClose(null));
    }

    [TestMethod]
    public void InFlightOctoberJob_CanBeCancelledWithoutOverwritingExistingResults()
    {
        var job = AiEmailAnalysisJob.Create(
            Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null,
            "/api/internal/data-extraction/ai-email-requests/123",
            "hash", "correlation", 2);
        job.MarkProcessing("worker-a", DateTime.UtcNow.AddMinutes(10));
        job.CancelHistoricalMonth();

        Assert.AreEqual(AiEmailAnalysisJobStatus.Cancelled, job.Status);
        Assert.IsNull(job.LeaseOwner);
        Assert.IsNotNull(job.CompletedAtUtc);
        Assert.ThrowsException<InvalidOperationException>(
            () => job.MarkCompleted(Guid.NewGuid(), """{"confidence":85}"""));
        Assert.AreEqual(AiEmailAnalysisJobStatus.Cancelled, job.Status);
    }
}
