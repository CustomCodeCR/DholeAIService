using CustomCodeFramework.Core.Results;

namespace Dhole.AI.Application.Shared;

public static class AiApplicationErrors
{
    public static readonly Error ConnectionAlreadyExists = new(
        "AI.ConnectionAlreadyExists",
        "Ya existe una conexión de inteligencia artificial con ese nombre."
    );

    public static readonly Error InvalidConnection = new(
        "AI.InvalidConnection",
        "La configuración de la conexión no es válida."
    );

    public static readonly Error ConnectionIsInactive = new(
        "AI.ConnectionIsInactive",
        "La conexión de inteligencia artificial está inactiva."
    );

    public static readonly Error ProviderNotSupported = new(
        "AI.ProviderNotSupported",
        "El proveedor de inteligencia artificial no está soportado."
    );

    public static readonly Error ProviderOperationFailed = new(
        "AI.ProviderOperationFailed",
        "No fue posible completar la operación con el proveedor."
    );

    public static readonly Error ProviderTimeout = new(
        "AI.ProviderTimeout",
        "El proveedor de inteligencia artificial superó el tiempo máximo configurado."
    );

    public static readonly Error ModelAlreadyExists = new(
        "AI.ModelAlreadyExists",
        "El modelo ya está registrado para la conexión seleccionada."
    );

    public static readonly Error InvalidModel = new(
        "AI.InvalidModel",
        "La configuración del modelo no es válida."
    );

    public static readonly Error ModelIsInactive = new(
        "AI.ModelIsInactive",
        "El modelo de inteligencia artificial está inactivo."
    );

    public static readonly Error ProfileAlreadyExists = new(
        "AI.ProfileAlreadyExists",
        "Ya existe un perfil con esa clave."
    );

    public static readonly Error InvalidProfile = new(
        "AI.InvalidProfile",
        "La configuración del perfil no es válida."
    );

    public static readonly Error ProfileIsInactive = new(
        "AI.ProfileIsInactive",
        "El perfil de inteligencia artificial está inactivo."
    );

    public static readonly Error ProfileHasInvalidModels = new(
        "AI.ProfileHasInvalidModels",
        "Uno o más modelos configurados en el perfil no existen o están inactivos."
    );

    public static readonly Error PromptTemplateAlreadyExists = new(
        "AI.PromptTemplateAlreadyExists",
        "Ya existe una plantilla de prompt con esa clave."
    );

    public static readonly Error InvalidPromptTemplate = new(
        "AI.InvalidPromptTemplate",
        "La configuración de la plantilla de prompt no es válida."
    );

    public static readonly Error PromptTemplateIsInactive = new(
        "AI.PromptTemplateIsInactive",
        "La plantilla de prompt está inactiva."
    );

    public static readonly Error MissingPromptVariable = new(
        "AI.MissingPromptVariable",
        "Faltan variables requeridas por la plantilla de prompt."
    );

    public static readonly Error NoModelAvailable = new(
        "AI.NoModelAvailable",
        "No existe un modelo disponible para ejecutar el perfil."
    );

    public static readonly Error ModelCapabilityNotSupported = new(
        "AI.ModelCapabilityNotSupported",
        "Ningún modelo configurado soporta la capacidad solicitada."
    );

    public static readonly Error ExecutionFailed = new(
        "AI.ExecutionFailed",
        "No fue posible completar la ejecución de inteligencia artificial."
    );

    public static readonly Error InvalidStructuredOutput = new(
        "AI.InvalidStructuredOutput",
        "La respuesta del modelo no contiene un JSON válido."
    );

    public static readonly Error ExecutionCannotBeCancelled = new(
        "AI.ExecutionCannotBeCancelled",
        "El estado actual de la ejecución no permite cancelarla."
    );
}
