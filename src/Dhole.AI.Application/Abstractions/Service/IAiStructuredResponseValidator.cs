using CustomCodeFramework.Core.Results;

namespace Dhole.AI.Application.Abstractions.Services;

public interface IAiStructuredResponseValidator
{
    Result<string> Validate(string content, string? jsonSchema);
}
