using System.Text.Json;
using CustomCodeFramework.Core.Results;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Shared;

namespace Dhole.AI.Application.Services;

public sealed class AiStructuredResponseValidator : IAiStructuredResponseValidator
{
    public Result<string> Validate(string content, string? jsonSchema)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
        }

        var normalized = RemoveMarkdownFence(content);

        try
        {
            using var response = JsonDocument.Parse(normalized);

            if (!string.IsNullOrWhiteSpace(jsonSchema))
            {
                using var schema = JsonDocument.Parse(jsonSchema);
            }

            return Result.Success(response.RootElement.GetRawText());
        }
        catch (JsonException)
        {
            return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
        }
    }

    private static string RemoveMarkdownFence(string content)
    {
        var value = content.Trim();

        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var firstLineBreak = value.IndexOf('\n');

        if (firstLineBreak >= 0)
        {
            value = value[(firstLineBreak + 1)..];
        }

        if (value.EndsWith("```", StringComparison.Ordinal))
        {
            value = value[..^3];
        }

        return value.Trim();
    }
}
