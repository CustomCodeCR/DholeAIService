using CustomCodeFramework.Core.Domain.Entities;
using Dhole.AI.Domain.Models.Enums;
using Dhole.AI.Domain.Models.Events;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Domain.Models.Entities;

public sealed class AiModel : SoftDeletableAggregateRoot<Guid>
{
    private AiModel() { }

    private AiModel(
        Guid id,
        Guid connectionId,
        string externalModelId,
        string name,
        AiModelCapability capabilities,
        int? contextWindow,
        int? maximumOutputTokens,
        decimal? inputCostPerMillionTokens,
        decimal? outputCostPerMillionTokens,
        bool isLocal,
        Guid? createdBy
    )
        : base(id)
    {
        Apply(
            connectionId,
            externalModelId,
            name,
            capabilities,
            contextWindow,
            maximumOutputTokens,
            inputCostPerMillionTokens,
            outputCostPerMillionTokens,
            isLocal
        );

        Status = AiModelStatus.Unknown;
        IsActive = true;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public Guid ConnectionId { get; private set; }

    public string ExternalModelId { get; private set; } = string.Empty;

    public string Name { get; private set; } = string.Empty;

    public AiModelCapability Capabilities { get; private set; }

    public int? ContextWindow { get; private set; }

    public int? MaximumOutputTokens { get; private set; }

    public decimal? InputCostPerMillionTokens { get; private set; }

    public decimal? OutputCostPerMillionTokens { get; private set; }

    public bool IsLocal { get; private set; }

    public AiModelStatus Status { get; private set; }

    public DateTime? LastAvailabilityCheckAtUtc { get; private set; }

    public string? LastAvailabilityError { get; private set; }

    public bool IsActive { get; private set; }

    public static AiModel Create(
        Guid connectionId,
        string externalModelId,
        string name,
        AiModelCapability capabilities,
        int? contextWindow,
        int? maximumOutputTokens,
        decimal? inputCostPerMillionTokens,
        decimal? outputCostPerMillionTokens,
        bool isLocal,
        Guid? createdBy
    )
    {
        var model = new AiModel(
            Guid.NewGuid(),
            connectionId,
            externalModelId,
            name,
            capabilities,
            contextWindow,
            maximumOutputTokens,
            inputCostPerMillionTokens,
            outputCostPerMillionTokens,
            isLocal,
            createdBy
        );

        model.AddDomainEvent(
            new AiModelCreatedDomainEvent(model.Id, model.ConnectionId, model.Name, createdBy)
        );

        return model;
    }

    public void Update(
        Guid connectionId,
        string externalModelId,
        string name,
        AiModelCapability capabilities,
        int? contextWindow,
        int? maximumOutputTokens,
        decimal? inputCostPerMillionTokens,
        decimal? outputCostPerMillionTokens,
        bool isLocal,
        Guid? updatedBy
    )
    {
        EnsureNotDeleted();

        Apply(
            connectionId,
            externalModelId,
            name,
            capabilities,
            contextWindow,
            maximumOutputTokens,
            inputCostPerMillionTokens,
            outputCostPerMillionTokens,
            isLocal
        );

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(new AiModelUpdatedDomainEvent(Id, ConnectionId, Name, updatedBy));
    }

    public bool Supports(AiModelCapability capability)
    {
        return (Capabilities & capability) == capability;
    }

    public void MarkAvailable(DateTime checkedAtUtc)
    {
        EnsureNotDeleted();

        Status = AiModelStatus.Available;
        LastAvailabilityCheckAtUtc = checkedAtUtc;
        LastAvailabilityError = null;
    }

    public void MarkUnavailable(DateTime checkedAtUtc, string error)
    {
        EnsureNotDeleted();

        if (string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException(
                "Debe indicar el error de disponibilidad del modelo."
            );
        }

        Status = AiModelStatus.Unavailable;
        LastAvailabilityCheckAtUtc = checkedAtUtc;

        LastAvailabilityError = Truncate(error.Trim(), AiConstants.MaximumErrorLength);
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

        AddDomainEvent(new AiModelActivatedDomainEvent(Id, ConnectionId, Name, updatedBy));
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

        AddDomainEvent(new AiModelInactivatedDomainEvent(Id, ConnectionId, Name, updatedBy));
    }

    public void Delete(Guid? deletedBy)
    {
        if (IsDeleted)
        {
            return;
        }

        IsActive = false;

        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());

        AddDomainEvent(new AiModelDeletedDomainEvent(Id, ConnectionId, Name, deletedBy));
    }

    private void Apply(
        Guid connectionId,
        string externalModelId,
        string name,
        AiModelCapability capabilities,
        int? contextWindow,
        int? maximumOutputTokens,
        decimal? inputCostPerMillionTokens,
        decimal? outputCostPerMillionTokens,
        bool isLocal
    )
    {
        if (connectionId == Guid.Empty)
        {
            throw new InvalidOperationException("La conexión del modelo es obligatoria.");
        }

        if (string.IsNullOrWhiteSpace(externalModelId))
        {
            throw new InvalidOperationException(
                "El identificador externo del modelo es obligatorio."
            );
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("El nombre del modelo es obligatorio.");
        }

        if (name.Trim().Length > AiConstants.MaximumNameLength)
        {
            throw new InvalidOperationException(
                $"El nombre del modelo no puede superar "
                    + $"{AiConstants.MaximumNameLength} caracteres."
            );
        }

        if (capabilities == AiModelCapability.None)
        {
            throw new InvalidOperationException("El modelo debe tener al menos una capacidad.");
        }

        if (contextWindow is <= 0)
        {
            throw new InvalidOperationException("La ventana de contexto debe ser mayor que cero.");
        }

        if (maximumOutputTokens is <= 0)
        {
            throw new InvalidOperationException(
                "El máximo de tokens de salida debe ser mayor que cero."
            );
        }

        if (maximumOutputTokens > AiConstants.MaximumOutputTokensLimit)
        {
            throw new InvalidOperationException(
                $"El máximo de tokens de salida no puede superar "
                    + $"{AiConstants.MaximumOutputTokensLimit}."
            );
        }

        if (
            contextWindow.HasValue
            && maximumOutputTokens.HasValue
            && maximumOutputTokens.Value > contextWindow.Value
        )
        {
            throw new InvalidOperationException(
                "El máximo de tokens de salida no puede superar " + "la ventana de contexto."
            );
        }

        if (inputCostPerMillionTokens is < 0m || outputCostPerMillionTokens is < 0m)
        {
            throw new InvalidOperationException("Los costos del modelo no pueden ser negativos.");
        }

        ConnectionId = connectionId;
        ExternalModelId = externalModelId.Trim();
        Name = name.Trim();
        Capabilities = capabilities;
        ContextWindow = contextWindow;
        MaximumOutputTokens = maximumOutputTokens;
        InputCostPerMillionTokens = inputCostPerMillionTokens;
        OutputCostPerMillionTokens = outputCostPerMillionTokens;
        IsLocal = isLocal;
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("El modelo fue eliminado.");
        }
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
