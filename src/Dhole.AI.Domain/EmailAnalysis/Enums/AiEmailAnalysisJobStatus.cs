namespace Dhole.AI.Domain.EmailAnalysis.Enums;

public enum AiEmailAnalysisJobStatus
{
    Pending = 1,
    Processing = 2,
    Completed = 3,
    RetryScheduled = 4,
    Failed = 5,
}
