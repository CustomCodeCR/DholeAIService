using CustomCodeFramework.Core.Results;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Domain.PromptTemplates.Entities;

namespace Dhole.AI.Application.Abstractions.Services;

public sealed record AiPromptVariable(string Name, string Value);

public sealed record AiCompiledPrompt(IReadOnlyCollection<AiProviderMessage> Messages);

public interface IAiPromptCompiler
{
    Result<AiCompiledPrompt> Compile(
        AiPromptTemplate? template,
        IReadOnlyCollection<AiProviderMessage> messages,
        IReadOnlyCollection<AiPromptVariable>? variables
    );
}
