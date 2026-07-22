using System.Text.Json;
using System.Text.Json.Nodes;
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

        try
        {
            var response = ParseResponseNode(RemoveMarkdownFence(content));
            if (response is null)
            {
                return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
            }

            JsonObject? schema = null;
            if (!string.IsNullOrWhiteSpace(jsonSchema))
            {
                schema = JsonNode.Parse(jsonSchema) as JsonObject;
                if (schema is null)
                {
                    return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
                }

                response = NormalizeEnvelope(response, schema);
                if (!MatchesRootType(response, schema))
                {
                    return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
                }
            }

            return Result.Success(
                response.ToJsonString(new JsonSerializerOptions { WriteIndented = false })
            );
        }
        catch (JsonException)
        {
            return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
        }
    }

    private static JsonNode? ParseResponseNode(string content)
    {
        var candidate = content;

        for (var level = 0; level < 3; level++)
        {
            var node = JsonNode.Parse(candidate);
            if (node is not JsonValue value || !value.TryGetValue<string>(out var nested))
            {
                return node;
            }

            if (string.IsNullOrWhiteSpace(nested))
            {
                return null;
            }

            candidate = RemoveMarkdownFence(nested);
        }

        return null;
    }

    private static JsonNode NormalizeEnvelope(JsonNode response, JsonObject schema)
    {
        var expectedType = ReadSchemaType(schema);
        var properties = schema["properties"] as JsonObject;

        if (expectedType == "object" && response is JsonArray array && properties?["rows"] is not null)
        {
            return CreateRowsEnvelope(array, properties);
        }

        if (response is not JsonObject root || expectedType != "object")
        {
            return response;
        }

        if (!ContainsRows(root))
        {
            foreach (var wrapperName in new[] { "data", "result", "output", "response", "payload", "content", "jsonContent" })
            {
                if (!TryGetProperty(root, wrapperName, out var nested) || nested is null)
                {
                    continue;
                }

                if (nested is JsonValue nestedValue && nestedValue.TryGetValue<string>(out var text))
                {
                    nested = ParseResponseNode(RemoveMarkdownFence(text));
                }

                if (nested is null)
                {
                    continue;
                }

                var normalized = NormalizeEnvelope(nested, schema);
                if (normalized is JsonObject normalizedObject && ContainsRows(normalizedObject))
                {
                    root = normalizedObject;
                    break;
                }

                if (normalized is JsonArray normalizedArray && properties?["rows"] is not null)
                {
                    root = CreateRowsEnvelope(normalizedArray, properties);
                    break;
                }
            }
        }

        if (!ContainsRows(root) && properties?["rows"] is not null)
        {
            foreach (var alias in new[] { "rates", "tariffs", "pricingRows", "items", "records", "results", "tarifas" })
            {
                if (!TryGetProperty(root, alias, out var rows) || rows is null)
                {
                    continue;
                }

                if (rows is JsonArray)
                {
                    root["rows"] = rows.DeepClone();
                    break;
                }

                if (rows is JsonObject row)
                {
                    root["rows"] = new JsonArray(row.DeepClone());
                    break;
                }

                if (rows is JsonValue value && value.TryGetValue<string>(out var text))
                {
                    var parsedRows = ParseResponseNode(RemoveMarkdownFence(text));
                    if (parsedRows is JsonArray parsedArray)
                    {
                        root["rows"] = parsedArray.DeepClone();
                        break;
                    }
                }
            }
        }

        AddEnvelopeDefaults(root, properties);
        return root;
    }

    private static JsonObject CreateRowsEnvelope(JsonArray rows, JsonObject properties)
    {
        var envelope = new JsonObject { ["rows"] = rows.DeepClone() };
        AddEnvelopeDefaults(envelope, properties);
        return envelope;
    }

    private static void AddEnvelopeDefaults(JsonObject root, JsonObject? properties)
    {
        if (properties is null)
        {
            return;
        }

        if (properties["success"] is not null && !TryGetProperty(root, "success", out _))
        {
            root["success"] = root["rows"] is JsonArray rows && rows.Count > 0;
        }

        if (properties["confidence"] is not null && !TryGetProperty(root, "confidence", out _))
        {
            root["confidence"] = 0;
        }

        if (properties["warnings"] is not null && !TryGetProperty(root, "warnings", out _))
        {
            root["warnings"] = new JsonArray();
        }
    }

    private static bool ContainsRows(JsonObject root)
    {
        return TryGetProperty(root, "rows", out var rows) && rows is JsonArray;
    }

    private static bool MatchesRootType(JsonNode response, JsonObject schema)
    {
        return ReadSchemaType(schema) switch
        {
            "object" => response is JsonObject,
            "array" => response is JsonArray,
            "string" => response is JsonValue value && value.TryGetValue<string>(out _),
            "number" or "integer" => response is JsonValue number
                && (number.TryGetValue<decimal>(out _) || number.TryGetValue<double>(out _)),
            "boolean" => response is JsonValue boolean && boolean.TryGetValue<bool>(out _),
            _ => true,
        };
    }

    private static string? ReadSchemaType(JsonObject schema)
    {
        if (schema["type"] is JsonValue value && value.TryGetValue<string>(out var type))
        {
            return type;
        }

        if (schema["type"] is JsonArray types)
        {
            return types
                .OfType<JsonValue>()
                .Select(item => item.TryGetValue<string>(out var type) ? type : null)
                .FirstOrDefault(type => type is not null && type != "null");
        }

        return null;
    }

    private static bool TryGetProperty(JsonObject root, string name, out JsonNode? value)
    {
        foreach (var property in root)
        {
            if (string.Equals(property.Key, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = null;
        return false;
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
