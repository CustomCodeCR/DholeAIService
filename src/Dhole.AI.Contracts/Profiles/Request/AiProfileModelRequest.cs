namespace Dhole.AI.Contracts.Profiles.Request;

public sealed record AiProfileModelRequest(Guid ModelId, int Priority, bool IsFallback);
