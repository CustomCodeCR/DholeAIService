namespace Dhole.AI.Domain.Executions.Enums;

public enum AiFinishReason
{
    Unknown = 0,
    Stop = 1,
    Length = 2,
    ContentFilter = 3,
    ToolCall = 4,
    Error = 5,
    Cancelled = 6,
}
