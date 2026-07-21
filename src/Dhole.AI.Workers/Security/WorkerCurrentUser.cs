
using CustomCodeFramework.Core.Abstractions;

namespace Dhole.AI.Workers.Security;

internal sealed class WorkerCurrentUser : ICurrentUser
{
    public bool IsAuthenticated => false;
    public string? UserId => null;
    public string? Email => null;
    public string? UserName => "Dhole.AI.Worker";
    public string? UserType => "Worker";
    public string? SessionId => null;
    public int? TokenVersion => null;
    public IReadOnlyCollection<string> Roles => [];
    public IReadOnlyCollection<string> Permissions => [];
    public IReadOnlyCollection<string> Scopes => [];
}
