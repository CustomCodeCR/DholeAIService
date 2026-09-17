using System.Text.Json.Nodes;
using Dhole.AI.Application.Abstractions.Providers.Models;
using Dhole.AI.Infrastructure.Providers.Common;

namespace Dhole.AI.Infrastructure.Providers.Ollama;

internal static class OllamaRequestMapper
{
    private const int DefaultMaximumContextTokens = 16_384;
    private const int Qwen35MaximumContextTokens = 32_768;
    private const int ContextSafetyMarginTokens = 1_024;
    private const int LegacyLlamaMaximumOutputTokens = 1_536;
    private const int LegacyLlamaMaximumContextTokens = 8_192;

    public static JsonObject CreateChatPayload(
        AiProviderChatRequest request,
        string model,
        bool stream
    )
    {
        var messages = new JsonArray();

        foreach (var message in request.Messages)
        {
            var mapped = new JsonObject
            {
                ["role"] = NormalizeRole(message.Role),
                ["content"] = message.Content,
            };

            if (message.Images?.Count > 0)
            {
                var images = new JsonArray();
                foreach (var image in message.Images)
                {
                    images.Add(image.Base64Data);
                }

                mapped["images"] = images;
            }

            messages.Add(mapped);
        }

        var outputTokens = CalculateOutputTokens(request, model);
        var payload = new JsonObject
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = stream,
            ["options"] = new JsonObject
            {
                ["temperature"] = (double)request.Temperature,
                ["num_predict"] = outputTokens,
                ["num_ctx"] = CalculateContextWindow(request, outputTokens, model),
            },
        };

        // Structured email formatting is a batch operation. Unloading the model after
        // the request prevents fallback models from remaining resident in RAM together.
        if (request.RequiresStructuredOutput)
        {
            payload["keep_alive"] = 0;
        }
        else
        {
            payload["keep_alive"] = "15m";
        }

        if (request.RequiresStructuredOutput)
        {
            payload["think"] = false;

            // The full JSON-schema grammar is expensive for legacy Llama 3 models and
            // can make Ollama exhaust memory or spend the complete timeout compiling /
            // decoding the grammar. JSON mode is still validated by Dhole afterwards.
            payload["format"] = RequiresLightweightJsonMode(model)
                ? JsonValue.Create("json")
                : ProviderJson.ParseNode(request.JsonSchema) ?? JsonValue.Create("json");
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

    private static int CalculateOutputTokens(
        AiProviderChatRequest request,
        string model
    )
    {
        var requested = Math.Max(128, request.MaximumOutputTokens);
        var maximumContextTokens = GetMaximumContextTokens(model);
        var inputCharacters = request.Messages.Sum(message => message.Content?.Length ?? 0);
        var estimatedInputTokens = (int)Math.Ceiling(inputCharacters / 3.5d);

        // num_predict shares the context window with the prompt. Pricing extraction now
        // carries source text, catalog hints and supervised feedback, so an 8K context can
        // leave Qwen with only a few hundred output tokens and truncate the JSON response.
        var availableOutputTokens = Math.Max(
            128,
            maximumContextTokens - estimatedInputTokens - ContextSafetyMarginTokens
        );

        var outputTokens = Math.Min(requested, availableOutputTokens);
        if (IsLegacyLlama(model))
        {
            outputTokens = Math.Min(outputTokens, LegacyLlamaMaximumOutputTokens);
        }

        return outputTokens;
    }

    private static int CalculateContextWindow(
        AiProviderChatRequest request,
        int outputTokens,
        string model
    )
    {
        var inputCharacters = request.Messages.Sum(message => message.Content?.Length ?? 0);
        var estimatedInputTokens = (int)Math.Ceiling(inputCharacters / 3.5d);
        var requiredTokens = estimatedInputTokens + outputTokens + ContextSafetyMarginTokens;
        var rounded = (int)Math.Ceiling(requiredTokens / 1_024d) * 1_024;
        var maximum = GetMaximumContextTokens(model);

        return Math.Clamp(rounded, 4_096, maximum);
    }

    private static int GetMaximumContextTokens(string model)
    {
        if (IsLegacyLlama(model))
        {
            return LegacyLlamaMaximumContextTokens;
        }

        return IsQwen35(model)
            ? Qwen35MaximumContextTokens
            : DefaultMaximumContextTokens;
    }

    private static bool RequiresLightweightJsonMode(string model)
    {
        return IsLegacyLlama(model);
    }

    private static bool IsQwen35(string model)
    {
        var normalized = model.Trim().ToLowerInvariant();
        return normalized.StartsWith("qwen3.5:", StringComparison.Ordinal)
            || normalized.Contains("qwen3.5", StringComparison.Ordinal);
    }

    private static bool IsLegacyLlama(string model)
    {
        var normalized = model.Trim().ToLowerInvariant();
        return normalized.StartsWith("llama3:", StringComparison.Ordinal)
            || normalized.StartsWith("llama3.1:", StringComparison.Ordinal)
            || normalized.StartsWith("llama3.2:", StringComparison.Ordinal);
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
