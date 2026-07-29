using System.Text.Json.Nodes;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.OpenAI;

internal static class OpenAiRequestMapper
{
    public static JsonObject CreateResponsePayload(
        AiProviderChatRequest request,
        string model,
        bool stream
    )
    {
        var input = new JsonArray();

        foreach (var message in request.Messages)
        {
            JsonNode content = JsonValue.Create(message.Content)!;

            if (message.Images?.Count > 0)
            {
                var parts = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "input_text",
                        ["text"] = message.Content,
                    },
                };

                foreach (var image in message.Images)
                {
                    parts.Add(new JsonObject
                    {
                        ["type"] = "input_image",
                        ["image_url"] = BuildDataUrl(image),
                    });
                }

                content = parts;
            }

            input.Add(
                new JsonObject
                {
                    ["role"] = NormalizeRole(message.Role),
                    ["content"] = content,
                }
            );
        }

        var payload = new JsonObject
        {
            ["model"] = model,
            ["input"] = input,
            ["temperature"] = (double)request.Temperature,
            ["max_output_tokens"] = request.MaximumOutputTokens,
            ["stream"] = stream,
            ["store"] = false,
        };

        if (request.RequiresStructuredOutput)
        {
            var format = new JsonObject { ["type"] = "json_object" };

            var schema = ProviderJson.ParseNode(request.JsonSchema);

            if (schema is not null)
            {
                format = new JsonObject
                {
                    ["type"] = "json_schema",
                    ["name"] = "dhole_response",
                    ["strict"] = true,
                    ["schema"] = schema,
                };
            }

            payload["text"] = new JsonObject { ["format"] = format };
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

        return new JsonObject
        {
            ["model"] = model,
            ["input"] = inputs,
            ["encoding_format"] = "float",
        };
    }

    private static string BuildDataUrl(AiProviderImage image)
    {
        return $"data:{image.MimeType};base64,{image.Base64Data}";
    }

    private static string NormalizeRole(string role)
    {
        return role.Trim().ToLowerInvariant() switch
        {
            "developer" => "developer",
            "system" => "system",
            "assistant" => "assistant",
            _ => "user",
        };
    }
}
