using System.Text.Json;
using CustomCodeFramework.Core.Results;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Shared;
using Dhole.AI.Domain.PromptTemplates.Entities;

namespace Dhole.AI.Application.Services;

public sealed class AiPromptCompiler : IAiPromptCompiler
{
    public Result<AiCompiledPrompt> Compile(
        AiPromptTemplate? template,
        IReadOnlyCollection<AiProviderMessage> messages,
        IReadOnlyCollection<AiPromptVariable>? variables
    )
    {
        var values = (variables ?? [])
            .GroupBy(item => item.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(
                group => group.Key,
                group => group.Last().Value,
                StringComparer.OrdinalIgnoreCase
            );

        var result = new List<AiProviderMessage>();

        if (template is not null)
        {
            var requiredVariables = ParseRequiredVariables(template.VariablesJson);

            var missing = requiredVariables.Where(name => !values.ContainsKey(name)).ToArray();

            if (missing.Length > 0)
            {
                return Result.Failure<AiCompiledPrompt>(AiApplicationErrors.MissingPromptVariable);
            }

            if (!string.IsNullOrWhiteSpace(template.SystemPrompt))
            {
                result.Add(new AiProviderMessage("system", Render(template.SystemPrompt, values)));
            }

            if (!string.IsNullOrWhiteSpace(template.UserPromptTemplate))
            {
                result.Add(
                    new AiProviderMessage("user", Render(template.UserPromptTemplate, values))
                );
            }
        }

        result.AddRange(
            messages.Select(message => new AiProviderMessage(
                NormalizeRole(message.Role),
                message.Content
            ))
        );

        if (result.Count == 0)
        {
            return Result.Failure<AiCompiledPrompt>(AiApplicationErrors.InvalidPromptTemplate);
        }

        return Result.Success(new AiCompiledPrompt(result));
    }

    private static IReadOnlyCollection<string> ParseRequiredVariables(string? variablesJson)
    {
        if (string.IsNullOrWhiteSpace(variablesJson))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(variablesJson) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static string Render(string template, IReadOnlyDictionary<string, string> variables)
    {
        var result = template;

        foreach (var variable in variables)
        {
            result = result.Replace(
                $"{{{{{variable.Key}}}}}",
                variable.Value,
                StringComparison.OrdinalIgnoreCase
            );
        }

        return result;
    }

    private static string NormalizeRole(string role)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "system" => "system",
            "assistant" => "assistant",
            "tool" => "tool",
            _ => "user",
        };
    }
}
