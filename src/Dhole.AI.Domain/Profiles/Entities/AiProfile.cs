using System.Text.Json;
using CustomCodeFramework.Core.Domain.Entities;
using Dhole.AI.Domain.Profiles.Enums;
using Dhole.AI.Domain.Profiles.Events;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Domain.Profiles.Entities;

public sealed class AiProfile : SoftDeletableAggregateRoot<Guid>
{
    private readonly List<AiProfileModel> _models = [];

    private AiProfile() { }

    private AiProfile(
        Guid id,
        string key,
        string name,
        string? description,
        Guid? promptTemplateId,
        AiRoutingMode routingMode,
        AiResponseFormat responseFormat,
        decimal temperature,
        int maximumOutputTokens,
        int timeoutSeconds,
        string? jsonSchema,
        Guid? createdBy
    )
        : base(id)
    {
        Apply(
            key,
            name,
            description,
            promptTemplateId,
            routingMode,
            responseFormat,
            temperature,
            maximumOutputTokens,
            timeoutSeconds,
            jsonSchema
        );

        IsActive = false;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public Guid? PromptTemplateId { get; private set; }

    public AiRoutingMode RoutingMode { get; private set; }

    public AiResponseFormat ResponseFormat { get; private set; }

    public decimal Temperature { get; private set; }

    public int MaximumOutputTokens { get; private set; }

    public int TimeoutSeconds { get; private set; }

    public string? JsonSchema { get; private set; }

    public bool IsActive { get; private set; }

    public IReadOnlyCollection<AiProfileModel> Models => _models.AsReadOnly();

    public static AiProfile Create(
        string key,
        string name,
        string? description,
        Guid? promptTemplateId,
        AiRoutingMode routingMode,
        AiResponseFormat responseFormat,
        decimal temperature,
        int maximumOutputTokens,
        int timeoutSeconds,
        string? jsonSchema,
        Guid? createdBy
    )
    {
        var profile = new AiProfile(
            Guid.NewGuid(),
            key,
            name,
            description,
            promptTemplateId,
            routingMode,
            responseFormat,
            temperature,
            maximumOutputTokens,
            timeoutSeconds,
            jsonSchema,
            createdBy
        );

        profile.AddDomainEvent(
            new AiProfileCreatedDomainEvent(profile.Id, profile.Key, profile.Name, createdBy)
        );

        return profile;
    }

    public void Update(
        string key,
        string name,
        string? description,
        Guid? promptTemplateId,
        AiRoutingMode routingMode,
        AiResponseFormat responseFormat,
        decimal temperature,
        int maximumOutputTokens,
        int timeoutSeconds,
        string? jsonSchema,
        Guid? updatedBy
    )
    {
        EnsureNotDeleted();

        Apply(
            key,
            name,
            description,
            promptTemplateId,
            routingMode,
            responseFormat,
            temperature,
            maximumOutputTokens,
            timeoutSeconds,
            jsonSchema
        );

        if (_models.Count > 0)
        {
            ValidateConfiguredModels(_models, RoutingMode);
        }

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(new AiProfileUpdatedDomainEvent(Id, Key, Name, updatedBy));
    }

    public void ConfigureModels(
        IEnumerable<(Guid ModelId, int Priority, bool IsFallback)> models,
        Guid? updatedBy
    )
    {
        EnsureNotDeleted();
        ArgumentNullException.ThrowIfNull(models);

        var configurations = models.ToArray();

        if (configurations.Length == 0)
        {
            throw new InvalidOperationException(
                "El perfil debe tener al menos un modelo configurado."
            );
        }

        if (configurations.Any(item => item.ModelId == Guid.Empty))
        {
            throw new InvalidOperationException(
                "Todos los modelos configurados deben ser válidos."
            );
        }

        if (configurations.Any(item => item.Priority <= 0))
        {
            throw new InvalidOperationException(
                "Todas las prioridades deben ser mayores que cero."
            );
        }

        if (configurations.Select(item => item.ModelId).Distinct().Count() != configurations.Length)
        {
            throw new InvalidOperationException(
                "No se puede configurar el mismo modelo más de una vez."
            );
        }

        if (
            configurations.Select(item => item.Priority).Distinct().Count() != configurations.Length
        )
        {
            throw new InvalidOperationException(
                "No se pueden repetir prioridades dentro del perfil."
            );
        }

        if (configurations.All(item => item.IsFallback))
        {
            throw new InvalidOperationException(
                "El perfil debe tener al menos un modelo principal."
            );
        }

        var newModels = configurations
            .OrderBy(item => item.Priority)
            .Select(item => AiProfileModel.Create(Id, item.ModelId, item.Priority, item.IsFallback))
            .ToArray();

        ValidateConfiguredModels(newModels, RoutingMode);

        _models.Clear();
        _models.AddRange(newModels);

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(
            new AiProfileModelsChangedDomainEvent(
                Id,
                Key,
                _models.Select(item => item.ModelId).ToArray(),
                updatedBy
            )
        );
    }

    public void Activate(Guid? updatedBy)
    {
        EnsureNotDeleted();

        if (IsActive)
        {
            return;
        }

        ValidateConfiguredModels(_models, RoutingMode);

        IsActive = true;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(new AiProfileActivatedDomainEvent(Id, Key, updatedBy));
    }

    public void Inactivate(Guid? updatedBy)
    {
        EnsureNotDeleted();

        if (!IsActive)
        {
            return;
        }

        IsActive = false;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(new AiProfileInactivatedDomainEvent(Id, Key, updatedBy));
    }

    public void Delete(Guid? deletedBy)
    {
        if (IsDeleted)
        {
            return;
        }

        IsActive = false;

        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());

        AddDomainEvent(new AiProfileDeletedDomainEvent(Id, Key, deletedBy));
    }

    private void Apply(
        string key,
        string name,
        string? description,
        Guid? promptTemplateId,
        AiRoutingMode routingMode,
        AiResponseFormat responseFormat,
        decimal temperature,
        int maximumOutputTokens,
        int timeoutSeconds,
        string? jsonSchema
    )
    {
        Key = NormalizeKey(key);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("El nombre del perfil es obligatorio.");
        }

        if (name.Trim().Length > AiConstants.MaximumNameLength)
        {
            throw new InvalidOperationException(
                $"El nombre del perfil no puede superar "
                    + $"{AiConstants.MaximumNameLength} caracteres."
            );
        }

        var normalizedDescription = Normalize(description);

        if (normalizedDescription?.Length > AiConstants.MaximumDescriptionLength)
        {
            throw new InvalidOperationException(
                $"La descripción no puede superar "
                    + $"{AiConstants.MaximumDescriptionLength} caracteres."
            );
        }

        if (
            temperature < AiConstants.MinimumTemperature
            || temperature > AiConstants.MaximumTemperature
        )
        {
            throw new InvalidOperationException(
                $"La temperatura debe estar entre "
                    + $"{AiConstants.MinimumTemperature} y "
                    + $"{AiConstants.MaximumTemperature}."
            );
        }

        if (maximumOutputTokens <= 0 || maximumOutputTokens > AiConstants.MaximumOutputTokensLimit)
        {
            throw new InvalidOperationException(
                $"El máximo de tokens de salida debe estar entre 1 y "
                    + $"{AiConstants.MaximumOutputTokensLimit}."
            );
        }

        if (
            timeoutSeconds < AiConstants.MinimumTimeoutSeconds
            || timeoutSeconds > AiConstants.MaximumTimeoutSeconds
        )
        {
            throw new InvalidOperationException(
                $"El timeout debe estar entre "
                    + $"{AiConstants.MinimumTimeoutSeconds} y "
                    + $"{AiConstants.MaximumTimeoutSeconds} segundos."
            );
        }

        var normalizedJsonSchema = Normalize(jsonSchema);

        if (responseFormat == AiResponseFormat.JsonSchema)
        {
            if (normalizedJsonSchema is null)
            {
                throw new InvalidOperationException(
                    "El esquema JSON es obligatorio para el formato JsonSchema."
                );
            }

            ValidateJson(normalizedJsonSchema);
        }

        Name = name.Trim();
        Description = normalizedDescription;

        PromptTemplateId = promptTemplateId == Guid.Empty ? null : promptTemplateId;

        RoutingMode = routingMode;
        ResponseFormat = responseFormat;
        Temperature = temperature;
        MaximumOutputTokens = maximumOutputTokens;
        TimeoutSeconds = timeoutSeconds;
        JsonSchema = normalizedJsonSchema;
    }

    private static void ValidateConfiguredModels(
        IReadOnlyCollection<AiProfileModel> models,
        AiRoutingMode routingMode
    )
    {
        if (models.Count == 0)
        {
            throw new InvalidOperationException(
                "El perfil debe tener al menos un modelo configurado."
            );
        }

        if (routingMode == AiRoutingMode.Fixed && models.Count != 1)
        {
            throw new InvalidOperationException(
                "Un perfil con enrutamiento fijo debe tener " + "exactamente un modelo."
            );
        }

        if (models.Select(item => item.Priority).Distinct().Count() != models.Count)
        {
            throw new InvalidOperationException(
                "No se pueden repetir prioridades dentro del perfil."
            );
        }
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("La clave del perfil es obligatoria.");
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > AiConstants.MaximumKeyLength)
        {
            throw new InvalidOperationException(
                $"La clave del perfil no puede superar "
                    + $"{AiConstants.MaximumKeyLength} caracteres."
            );
        }

        if (
            normalized.Any(character =>
                !char.IsLetterOrDigit(character) && character is not '.' and not '-' and not '_'
            )
        )
        {
            throw new InvalidOperationException(
                "La clave del perfil solo puede contener letras, "
                    + "números, puntos, guiones y guiones bajos."
            );
        }

        return normalized;
    }

    private static void ValidateJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "El esquema JSON del perfil no es válido.",
                exception
            );
        }
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("El perfil fue eliminado.");
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
