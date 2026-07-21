namespace Dhole.AI.Infrastructure.Secrets;

public interface IAiSecretSource
{
    bool CanResolve(string reference);

    Task<string?> ResolveAsync(string reference, CancellationToken cancellationToken = default);
}
