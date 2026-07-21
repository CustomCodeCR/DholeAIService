using System.Text.Json.Nodes;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Ollama;

internal static class OllamaRequestMapper
{
    public static JsonObject CreateChatPayload(
        AiProviderChatRequest request,
        string model,
        bool stream
    )
    {
        var messages = new JsonArray();

        foreach (var message in request.Messages)
        {
            messages.Add(
                new JsonObject
                {
                    ["role"] = NormalizeRole(message.Role),
                    ["content"] = message.Content,
                }
            );
        }

        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = stream,
            ["options"] = new JsonObject
            {
                ["temperature"] = (double)request.Temperature,
                ["num_predict"] = request.MaximumOutputTokens,
            },
        };

        if (request.RequiresStructuredOutput)
        {
            payload["format"] =
                ProviderJson.ParseNode(request.JsonSchema) ?? JsonValue.Create("json");
        }

        return payload;
    }

    public static JsonObject CreateEmbeddingPayload(
        AiProviderEmbeddingRequest request,
        string model
    )
    {
        var inputs = new JsonArray();

        foreach (var input in request.Inputs)
        {
            inputs.Add(input);
        }

        return new JsonObject { ["model"] = model, ["input"] = inputs };
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
