using System.Text.Json.Nodes;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Gemini;

internal static class GeminiRequestMapper
{
    public static JsonObject CreateGeneratePayload(AiProviderChatRequest request)
    {
        var contents = new JsonArray();

        var systemMessages = request
            .Messages.Where(message =>
                string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)
                || string.Equals(message.Role, "developer", StringComparison.OrdinalIgnoreCase)
            )
            .Select(message => message.Content)
            .ToArray();

        foreach (
            var message in request.Messages.Where(message =>
                !string.Equals(message.Role, "system", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(message.Role, "developer", StringComparison.OrdinalIgnoreCase)
            )
        )
        {
            contents.Add(
                new JsonObject
                {
                    ["role"] = NormalizeRole(message.Role),
                    ["parts"] = new JsonArray { new JsonObject { ["text"] = message.Content } },
                }
            );
        }

        var generationConfig = new JsonObject
        {
            ["temperature"] = (double)request.Temperature,
            ["maxOutputTokens"] = request.MaximumOutputTokens,
        };

        if (request.RequiresStructuredOutput)
        {
            generationConfig["responseMimeType"] = "application/json";

            var schema = ProviderJson.ParseNode(request.JsonSchema);

            if (schema is not null)
            {
                generationConfig["responseJsonSchema"] = schema;
            }
        }

        var payload = new JsonObject
        {
            ["contents"] = contents,
            ["generationConfig"] = generationConfig,
        };

        if (systemMessages.Length > 0)
        {
            payload["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray
                {
                    new JsonObject { ["text"] = string.Join(Environment.NewLine, systemMessages) },
                },
            };
        }

        return payload;
    }

    public static JsonObject CreateEmbeddingPayload(
        AiProviderEmbeddingRequest request,
        string modelResourceName
    )
    {
        var requests = new JsonArray();

        foreach (var input in request.Inputs)
        {
            requests.Add(
                new JsonObject
                {
                    ["model"] = modelResourceName,
                    ["content"] = new JsonObject
                    {
                        ["parts"] = new JsonArray { new JsonObject { ["text"] = input } },
                    },
                }
            );
        }

        return new JsonObject { ["requests"] = requests };
    }

    public static string NormalizeModelResourceName(string model)
    {
        return model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? model
            : $"models/{model}";
    }

    public static string NormalizeModelId(string model)
    {
        return model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? model["models/".Length..]
            : model;
    }

    private static string NormalizeRole(string role)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "assistant" => "model",
            "model" => "model",
            _ => "user",
        };
    }
}
