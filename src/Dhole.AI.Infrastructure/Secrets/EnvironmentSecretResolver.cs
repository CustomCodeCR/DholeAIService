namespace Dhole.AI.Infrastructure.Secrets;

internal sealed class EnvironmentSecretResolver : IAiSecretSource
{
    public bool CanResolve(string reference)
    {
        return reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase)
            || reference.StartsWith("env://", StringComparison.OrdinalIgnoreCase)
            || !reference.Contains(':', StringComparison.Ordinal);
    }

    public Task<string?> ResolveAsync(
        string reference,
        CancellationToken cancellationToken = default
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var variableName = Normalize(reference);

        if (string.IsNullOrWhiteSpace(variableName))
        {
            throw new InvalidOperationException("La referencia al secreto está vacía.");
        }

        var value = Environment.GetEnvironmentVariable(variableName);

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new InvalidOperationException(
                $"No se encontró la variable de entorno " + $"'{variableName}'."
            );
        }

        return Task.FromResult<string?>(value);
    }

    private static string Normalize(string reference)
    {
        if (reference.StartsWith("env://", StringComparison.OrdinalIgnoreCase))
        {
            return reference["env://".Length..].Trim();
        }

        if (reference.StartsWith("env:", StringComparison.OrdinalIgnoreCase))
        {
            return reference["env:".Length..].Trim();
        }

        return reference.Trim();
    }
}
