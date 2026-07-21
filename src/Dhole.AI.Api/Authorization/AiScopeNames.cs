
namespace Dhole.AI.Api.Authorization;

internal static class AiScopeNames
{
    // Connections
    public const string ConnectionCreate = "ai.connection.create";
    public const string ConnectionView = "ai.connection.view";
    public const string ConnectionUpdate = "ai.connection.update";
    public const string ConnectionDelete = "ai.connection.delete";
    public const string ConnectionSetActive = "ai.connection.set-active";
    public const string ConnectionTest = "ai.connection.test";
    public const string ConnectionDiscoverModels = "ai.connection.discover-models";

    // Models
    public const string ModelCreate = "ai.model.create";
    public const string ModelView = "ai.model.view";
    public const string ModelUpdate = "ai.model.update";
    public const string ModelDelete = "ai.model.delete";
    public const string ModelSetActive = "ai.model.set-active";

    // Profiles
    public const string ProfileCreate = "ai.profile.create";
    public const string ProfileView = "ai.profile.view";
    public const string ProfileUpdate = "ai.profile.update";
    public const string ProfileDelete = "ai.profile.delete";
    public const string ProfileSetActive = "ai.profile.set-active";
    public const string ProfileConfigureModels = "ai.profile.configure-models";

    // Prompt templates
    public const string PromptTemplateCreate = "ai.prompt-template.create";
    public const string PromptTemplateView = "ai.prompt-template.view";
    public const string PromptTemplateUpdate = "ai.prompt-template.update";
    public const string PromptTemplateDelete = "ai.prompt-template.delete";
    public const string PromptTemplateSetActive = "ai.prompt-template.set-active";

    // Executions
    public const string ExecutionView = "ai.execution.view";
    public const string ExecutionExecute = "ai.execution.execute";
    public const string ExecutionCancel = "ai.execution.cancel";
}
