namespace Dhole.AI.Domain.Models.Enums;

[Flags]
public enum AiModelCapability
{
    None = 0,
    Chat = 1,
    StructuredOutput = 2,
    Embeddings = 4,
    Vision = 8,
    Streaming = 16,
    ToolCalling = 32,
}
