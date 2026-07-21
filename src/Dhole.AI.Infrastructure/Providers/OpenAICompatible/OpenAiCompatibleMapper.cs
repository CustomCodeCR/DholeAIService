using System.Text.Json;
using System.Text.Json.Nodes;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.OpenAICompatible;

internal static class OpenAiCompatibleMapper
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
            ["temperature"] = (double)request.Temperature,
            ["max_tokens"] = request.MaximumOutputTokens,
            ["stream"] = stream,
        };

        if (request.RequiresStructuredOutput)
        {
            var schema = ProviderJson.ParseNode(request.JsonSchema);

            payload["response_format"] = schema is null
                ? new JsonObject { ["type"] = "json_object" }
                : new JsonObject
                {
                    ["type"] = "json_schema",
                    ["json_schema"] = new JsonObject
                    {
                        ["name"] = "dhole_response",
                        ["strict"] = true,
                        ["schema"] = schema,
                    },
                };
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

    public static AiProviderChatResponse ToChatResponse(JsonElement root)
    {
        var content = string.Empty;
        var finishReason = "Unknown";

        if (
            root.TryGetProperty("choices", out var choices)
            && choices.ValueKind == JsonValueKind.Array
        )
        {
            var first = choices.EnumerateArray().FirstOrDefault();

            if (
                first.ValueKind == JsonValueKind.Object
                && first.TryGetProperty("message", out var message)
                && message.TryGetProperty("content", out var contentProperty)
            )
            {
                content = contentProperty.GetString() ?? string.Empty;
            }

            if (
                first.ValueKind == JsonValueKind.Object
                && first.TryGetProperty("finish_reason", out var reason)
            )
            {
                finishReason = MapFinishReason(reason.GetString());
            }
        }

        var inputTokens = 0;
        var outputTokens = 0;

        if (root.TryGetProperty("usage", out var usage))
        {
            inputTokens = ProviderJson.GetInt32(usage, "prompt_tokens");

            outputTokens = ProviderJson.GetInt32(usage, "completion_tokens");
        }

        return new AiProviderChatResponse(
            content,
            inputTokens,
            outputTokens,
            finishReason,
            root.GetRawText()
        );
    }

    public static string? ReadStreamDelta(JsonElement root)
    {
        if (
            !root.TryGetProperty("choices", out var choices)
            || choices.ValueKind != JsonValueKind.Array
        )
        {
            return null;
        }

        var first = choices.EnumerateArray().FirstOrDefault();

        if (
            first.ValueKind != JsonValueKind.Object
            || !first.TryGetProperty("delta", out var delta)
            || !delta.TryGetProperty("content", out var content)
        )
        {
            return null;
        }

        return content.ValueKind == JsonValueKind.String ? content.GetString() : null;
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

    private static string MapFinishReason(string? reason)
    {
        return reason?.ToLowerInvariant() switch
        {
            "stop" => "Stop",
            "length" => "Length",
            "content_filter" => "ContentFilter",
            "tool_calls" => "ToolCall",
            _ => "Unknown",
        };
    }
}
