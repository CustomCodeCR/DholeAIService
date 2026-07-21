namespace Dhole.AI.Application.Auditing;

public static class AiAuditEventTypes
{
    public const string ConnectionCreated = "ai.connection.created";
    public const string ConnectionUpdated = "ai.connection.updated";
    public const string ConnectionDeleted = "ai.connection.deleted";
    public const string ConnectionActivated = "ai.connection.activated";
    public const string ConnectionInactivated = "ai.connection.inactivated";
    public const string ConnectionTested = "ai.connection.tested";
    public const string ConnectionModelsDiscovered = "ai.connection.models-discovered";

    public const string ModelCreated = "ai.model.created";
    public const string ModelUpdated = "ai.model.updated";
    public const string ModelDeleted = "ai.model.deleted";
    public const string ModelActivated = "ai.model.activated";
    public const string ModelInactivated = "ai.model.inactivated";

    public const string ProfileCreated = "ai.profile.created";
    public const string ProfileUpdated = "ai.profile.updated";
    public const string ProfileDeleted = "ai.profile.deleted";
    public const string ProfileActivated = "ai.profile.activated";
    public const string ProfileInactivated = "ai.profile.inactivated";
    public const string ProfileModelsConfigured = "ai.profile.models-configured";

    public const string PromptTemplateCreated = "ai.prompt-template.created";

    public const string PromptTemplateUpdated = "ai.prompt-template.updated";

    public const string PromptTemplateDeleted = "ai.prompt-template.deleted";

    public const string PromptTemplateActivated = "ai.prompt-template.activated";

    public const string PromptTemplateInactivated = "ai.prompt-template.inactivated";

    public const string ExecutionStarted = "ai.execution.started";
    public const string ExecutionCompleted = "ai.execution.completed";
    public const string ExecutionFailed = "ai.execution.failed";
    public const string ExecutionCancelled = "ai.execution.cancelled";
    public const string ExecutionFallbackUsed = "ai.execution.fallback-used";

    public const string ChatRequested = "ai.chat.requested";
    public const string ChatCompleted = "ai.chat.completed";
    public const string ChatFailed = "ai.chat.failed";
}
