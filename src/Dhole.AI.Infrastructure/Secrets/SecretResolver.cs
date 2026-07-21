using Dhole.AI.Application.Abstractions.Services;

namespace Dhole.AI.Infrastructure.Secrets;

public sealed class SecretResolver(IEnumerable<IAiSecretSource> sources) : IAiSecretResolver
{
    private readonly IReadOnlyCollection<IAiSecretSource> _sources = sources.ToArray();

    public async Task<string?> ResolveAsync(
        string? secretReference,
        CancellationToken cancellationToken = default
    )
    {
        if (string.IsNullOrWhiteSpace(secretReference))
        {
            return null;
        }

        var source = _sources.FirstOrDefault(item => item.CanResolve(secretReference));

        if (source is null)
        {
            throw new NotSupportedException(
                $"No existe un resolvedor para la referencia " + $"de secreto '{secretReference}'."
            );
        }

        return await source.ResolveAsync(secretReference, cancellationToken);
    }
}
