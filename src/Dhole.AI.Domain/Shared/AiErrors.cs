using CustomCodeFramework.Core.Results;

namespace Dhole.AI.Domain.Shared;

public static class AiErrors
{
    public static readonly Error ConnectionNotFound = new(
        "AI.ConnectionNotFound",
        "La conexión de inteligencia artificial no existe."
    );

    public static readonly Error ConnectionNameIsRequired = new(
        "AI.ConnectionNameIsRequired",
        "El nombre de la conexión es obligatorio."
    );

    public static readonly Error ConnectionBaseUrlIsRequired = new(
        "AI.ConnectionBaseUrlIsRequired",
        "La URL base de la conexión es obligatoria."
    );

    public static readonly Error ConnectionBaseUrlIsInvalid = new(
        "AI.ConnectionBaseUrlIsInvalid",
        "La URL base de la conexión no es válida."
    );

    public static readonly Error ConnectionIsInactive = new(
        "AI.ConnectionIsInactive",
        "La conexión de inteligencia artificial está inactiva."
    );

    public static readonly Error ModelNotFound = new(
        "AI.ModelNotFound",
        "El modelo de inteligencia artificial no existe."
    );

    public static readonly Error ModelExternalIdIsRequired = new(
        "AI.ModelExternalIdIsRequired",
        "El identificador externo del modelo es obligatorio."
    );

    public static readonly Error ModelNameIsRequired = new(
        "AI.ModelNameIsRequired",
        "El nombre del modelo es obligatorio."
    );

    public static readonly Error ModelIsInactive = new(
        "AI.ModelIsInactive",
        "El modelo de inteligencia artificial está inactivo."
    );

    public static readonly Error ModelIsUnavailable = new(
        "AI.ModelIsUnavailable",
        "El modelo de inteligencia artificial no está disponible."
    );

    public static readonly Error ProfileNotFound = new(
        "AI.ProfileNotFound",
        "El perfil de inteligencia artificial no existe."
    );

    public static readonly Error ProfileKeyIsRequired = new(
        "AI.ProfileKeyIsRequired",
        "La clave del perfil es obligatoria."
    );

    public static readonly Error ProfileNameIsRequired = new(
        "AI.ProfileNameIsRequired",
        "El nombre del perfil es obligatorio."
    );

    public static readonly Error ProfileHasNoModels = new(
        "AI.ProfileHasNoModels",
        "El perfil debe tener al menos un modelo configurado."
    );

    public static readonly Error ProfileJsonSchemaIsRequired = new(
        "AI.ProfileJsonSchemaIsRequired",
        "El esquema JSON es obligatorio para el formato JsonSchema."
    );

    public static readonly Error PromptTemplateNotFound = new(
        "AI.PromptTemplateNotFound",
        "La plantilla de prompt no existe."
    );

    public static readonly Error PromptTemplateKeyIsRequired = new(
        "AI.PromptTemplateKeyIsRequired",
        "La clave de la plantilla de prompt es obligatoria."
    );

    public static readonly Error PromptTemplateNameIsRequired = new(
        "AI.PromptTemplateNameIsRequired",
        "El nombre de la plantilla de prompt es obligatorio."
    );

    public static readonly Error PromptTemplateContentIsRequired = new(
        "AI.PromptTemplateContentIsRequired",
        "La plantilla debe contener un prompt de sistema o de usuario."
    );

    public static readonly Error ExecutionNotFound = new(
        "AI.ExecutionNotFound",
        "La ejecución de inteligencia artificial no existe."
    );

    public static readonly Error ExecutionInvalidStatus = new(
        "AI.ExecutionInvalidStatus",
        "El estado de la ejecución no permite realizar esta operación."
    );

    public static readonly Error ExecutionAttemptNotFound = new(
        "AI.ExecutionAttemptNotFound",
        "El intento de ejecución no existe."
    );
}
