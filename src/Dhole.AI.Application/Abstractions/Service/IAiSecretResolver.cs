namespace Dhole.AI.Application.Abstractions.Services;

public interface IAiSecretResolver
{
    Task<string?> ResolveAsync(
        string? secretReference,
        CancellationToken cancellationToken = default
    );
}
