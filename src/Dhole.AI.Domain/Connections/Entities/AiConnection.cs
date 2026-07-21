using CustomCodeFramework.Core.Domain.Entities;
using Dhole.AI.Domain.Connections.Enums;
using Dhole.AI.Domain.Connections.Events;
using Dhole.AI.Domain.Shared;

namespace Dhole.AI.Domain.Connections.Entities;

public sealed class AiConnection : SoftDeletableAggregateRoot<Guid>
{
    private AiConnection() { }

    private AiConnection(
        Guid id,
        string name,
        AiProviderType providerType,
        string baseUrl,
        string? secretReference,
        int timeoutSeconds,
        Guid? createdBy
    )
        : base(id)
    {
        Apply(name, providerType, baseUrl, secretReference, timeoutSeconds);

        Status = AiConnectionStatus.Unknown;
        IsActive = true;

        MarkAsCreated(DateTime.UtcNow, createdBy?.ToString());
    }

    public string Name { get; private set; } = string.Empty;

    public AiProviderType ProviderType { get; private set; }

    public string BaseUrl { get; private set; } = string.Empty;

    public string? SecretReference { get; private set; }

    public int TimeoutSeconds { get; private set; }

    public AiConnectionStatus Status { get; private set; }

    public DateTime? LastHealthCheckAtUtc { get; private set; }

    public string? LastHealthError { get; private set; }

    public bool IsActive { get; private set; }

    public static AiConnection Create(
        string name,
        AiProviderType providerType,
        string baseUrl,
        string? secretReference,
        int timeoutSeconds,
        Guid? createdBy
    )
    {
        var connection = new AiConnection(
            Guid.NewGuid(),
            name,
            providerType,
            baseUrl,
            secretReference,
            timeoutSeconds,
            createdBy
        );

        connection.AddDomainEvent(
            new AiConnectionCreatedDomainEvent(
                connection.Id,
                connection.Name,
                connection.ProviderType,
                createdBy
            )
        );

        return connection;
    }

    public void Update(
        string name,
        AiProviderType providerType,
        string baseUrl,
        string? secretReference,
        int timeoutSeconds,
        Guid? updatedBy
    )
    {
        EnsureNotDeleted();

        Apply(name, providerType, baseUrl, secretReference, timeoutSeconds);

        Status = AiConnectionStatus.Unknown;
        LastHealthCheckAtUtc = null;
        LastHealthError = null;

        MarkAsUpdated(DateTime.UtcNow, updatedBy?.ToString());

        AddDomainEvent(new AiConnectionUpdatedDomainEvent(Id, Name, ProviderType, updatedBy));
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

        AddDomainEvent(new AiConnectionActivatedDomainEvent(Id, Name, updatedBy));
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

        AddDomainEvent(new AiConnectionInactivatedDomainEvent(Id, Name, updatedBy));
    }

    public void MarkHealthy(DateTime checkedAtUtc)
    {
        EnsureNotDeleted();

        Status = AiConnectionStatus.Healthy;
        LastHealthCheckAtUtc = checkedAtUtc;
        LastHealthError = null;
    }

    public void MarkUnhealthy(DateTime checkedAtUtc, string error)
    {
        EnsureNotDeleted();

        if (string.IsNullOrWhiteSpace(error))
        {
            throw new InvalidOperationException("Debe indicar el error de la conexión.");
        }

        Status = AiConnectionStatus.Unhealthy;
        LastHealthCheckAtUtc = checkedAtUtc;

        LastHealthError = Truncate(error.Trim(), AiConstants.MaximumErrorLength);
    }

    public void Delete(Guid? deletedBy)
    {
        if (IsDeleted)
        {
            return;
        }

        IsActive = false;

        MarkAsDeleted(DateTime.UtcNow, deletedBy?.ToString());

        AddDomainEvent(new AiConnectionDeletedDomainEvent(Id, Name, deletedBy));
    }

    private void Apply(
        string name,
        AiProviderType providerType,
        string baseUrl,
        string? secretReference,
        int timeoutSeconds
    )
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("El nombre de la conexión es obligatorio.");
        }

        if (name.Trim().Length > AiConstants.MaximumNameLength)
        {
            throw new InvalidOperationException(
                $"El nombre de la conexión no puede superar "
                    + $"{AiConstants.MaximumNameLength} caracteres."
            );
        }

        if (string.IsNullOrWhiteSpace(baseUrl))
        {
            throw new InvalidOperationException("La URL base de la conexión es obligatoria.");
        }

        var normalizedBaseUrl = baseUrl.Trim().TrimEnd('/');

        if (
            !Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
        )
        {
            throw new InvalidOperationException("La URL base de la conexión no es válida.");
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

        Name = name.Trim();
        ProviderType = providerType;
        BaseUrl = normalizedBaseUrl;
        SecretReference = Normalize(secretReference);
        TimeoutSeconds = timeoutSeconds;
    }

    private void EnsureNotDeleted()
    {
        if (IsDeleted)
        {
            throw new InvalidOperationException("La conexión fue eliminada.");
        }
    }

    private static string? Normalize(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }
}
