using Dhole.AI.Domain.EmailAnalysis.Enums;
using Dhole.AI.Domain.Executions.Enums;

namespace Dhole.AI.Domain.EmailAnalysis;

/// <summary>
/// Separates a successful model execution from a failed downstream email
/// extraction handoff. The durable job status is retained for retries/audit.
/// </summary>
public static class AiEmailAnalysisPresentation
{
    public const string CompletedWithReview = nameof(CompletedWithReview);

    public static string Resolve(
        AiEmailAnalysisJobStatus jobStatus,
        AiExecutionStatus? executionStatus)
    {
        return jobStatus == AiEmailAnalysisJobStatus.Failed
            && executionStatus == AiExecutionStatus.Completed
            ? CompletedWithReview
            : jobStatus.ToString();
    }
}
