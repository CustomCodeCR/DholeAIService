using System.Text.Json;
using CustomCodeFramework.Core.Domain.Entities;
using Dhole.AI.Domain.PromptTemplates.Events;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Domain.PromptTemplates.Entities;

public sealed class AiPromptTemplate : SoftDeletableAggregateRoot<Guid>
{
    private AiPromptTemplate() { }

    private AiPromptTemplate(
        Guid id,
        string key,
        string name,
        string? description,
        string? systemPrompt,
        string? userPromptTemplate,
        string? variablesJson,
        Guid? createdBy
    )
        : base(id)
    {
        Apply(key, name, description, systemPrompt, userPromptTemplate, variablesJson);

        IsActive = true;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public string Key { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? SystemPrompt { get; private set; }

    public string? UserPromptTemplate { get; private set; }

    public string? VariablesJson { get; private set; }

    public bool IsActive { get; private set; }

    public static AiPromptTemplate Create(
        string key,
        string name,
        string? description,
        string? systemPrompt,
        string? userPromptTemplate,
        string? variablesJson,
        Guid? createdBy
    )
    {
        var template = new AiPromptTemplate(
            Guid.NewGuid(),
            key,
            name,
            description,
            systemPrompt,
            userPromptTemplate,
            variablesJson,
            createdBy
        );

        template.AddDomainEvent(
            new AiPromptTemplateCreatedDomainEvent(
                template.Id,
                template.Key,
                template.Name,
                createdBy
            )
        );

        return template;
    }

    public void Update(
        string key,
        string name,
        string? description,
        string? systemPrompt,
        string? userPromptTemplate,
        string? variablesJson,
        Guid? updatedBy
    )
    {
        EnsureNotDeleted();

        Apply(key, name, description, systemPrompt, userPromptTemplate, variablesJson);

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(new AiPromptTemplateUpdatedDomainEvent(Id, Key, Name, updatedBy));
    }

    public void Activate(Guid? updatedBy)
    {
        EnsureNotDeleted();

        if (IsActive)
        {
            return;
        }

        IsActive = true;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(new AiPromptTemplateActivatedDomainEvent(Id, Key, updatedBy));
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

        AddDomainEvent(new AiPromptTemplateInactivatedDomainEvent(Id, Key, updatedBy));
    }

    public void Delete(Guid? deletedBy)
    {
        if (IsDeleted)
        {
            return;
        }

        IsActive = false;

        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());

        AddDomainEvent(new AiPromptTemplateDeletedDomainEvent(Id, Key, deletedBy));
    }

    private void Apply(
        string key,
        string name,
        string? description,
        string? systemPrompt,
        string? userPromptTemplate,
        string? variablesJson
    )
    {
        var normalizedKey = NormalizeKey(key);

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("El nombre de la plantilla es obligatorio.");
        }

        if (name.Trim().Length > AiConstants.MaximumNameLength)
        {
            throw new InvalidOperationException(
                $"El nombre de la plantilla no puede superar "
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

        var normalizedSystemPrompt = Normalize(systemPrompt);

        var normalizedUserPromptTemplate = Normalize(userPromptTemplate);

        if (normalizedSystemPrompt is null && normalizedUserPromptTemplate is null)
        {
            throw new InvalidOperationException(
                "La plantilla debe contener un prompt de sistema " + "o de usuario."
            );
        }

        var normalizedVariablesJson = Normalize(variablesJson);

        if (normalizedVariablesJson is not null)
        {
            ValidateVariablesJson(normalizedVariablesJson);
        }

        Key = normalizedKey;
        Name = name.Trim();
        Description = normalizedDescription;
        SystemPrompt = normalizedSystemPrompt;
        UserPromptTemplate = normalizedUserPromptTemplate;
        VariablesJson = normalizedVariablesJson;
    }

    private static string NormalizeKey(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException("La clave de la plantilla es obligatoria.");
        }

        var normalized = value.Trim().ToLowerInvariant();

        if (normalized.Length > AiConstants.MaximumKeyLength)
        {
            throw new InvalidOperationException(
                $"La clave de la plantilla no puede superar "
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
                "La clave de la plantilla solo puede contener letras, "
                    + "números, puntos, guiones y guiones bajos."
            );
        }

        return normalized;
    }

    private static void ValidateVariablesJson(string value)
    {
        try
        {
            using var document = JsonDocument.Parse(value);

            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                throw new InvalidOperationException(
                    "Las variables de la plantilla deben representarse " + "como un arreglo JSON."
                );
            }

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (
                    element.ValueKind != JsonValueKind.String
                    || string.IsNullOrWhiteSpace(element.GetString())
                )
                {
                    throw new InvalidOperationException(
                        "Cada variable de la plantilla debe ser " + "una cadena válida."
                    );
                }
            }
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException(
                "Las variables de la plantilla no contienen " + "un JSON válido.",
                exception
            );
        }
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("La plantilla fue eliminada.");
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
