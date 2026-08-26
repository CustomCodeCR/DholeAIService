using System.Text.Json;
using System.Text.Json.Nodes;
using CustomCodeFramework.Core.Results;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Application.Shared;

namespace Dhole.AI.Application.Services;

public sealed class AiStructuredResponseValidator : IAiStructuredResponseValidator
{
    private static readonly JsonDocumentOptions TolerantJsonOptions = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    public Result<string> Validate(string content, string? jsonSchema)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
        }

        try
        {
            JsonObject? schema = null;
            if (!string.IsNullOrWhiteSpace(jsonSchema))
            {
                schema = JsonNode.Parse(jsonSchema) as JsonObject;
                if (schema is null)
                {
                    return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
                }
            }

            var response = ParseResponseNode(content, schema);
            if (response is null)
            {
                return Result.Failure<string>(AiApplicationErrors.InvalidStructuredOutput);
            }

            if (schema is not null)
            {
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

    private static JsonNode? ParseResponseNode(string content, JsonObject? schema)
    {
        var candidate = content;

        for (var level = 0; level < 3; level++)
        {
            var node = FindBestJsonNode(candidate, schema);
            if (node is null)
            {
                return null;
            }

            if (node is not JsonValue value || !value.TryGetValue<string>(out var nested))
            {
                return node;
            }

            if (string.IsNullOrWhiteSpace(nested))
            {
                return null;
            }

            candidate = nested;
        }

        return null;
    }

    private static JsonNode? FindBestJsonNode(string content, JsonObject? schema)
    {
        var candidate = RemoveMarkdownFence(content);
        if (TryParseNode(candidate, out var direct))
        {
            return direct;
        }

        JsonNode? best = null;
        var bestScore = int.MinValue;
        var bestLength = -1;

        foreach (var fragment in EnumerateJsonFragments(candidate))
        {
            if (!TryParseNode(fragment, out var parsed) || parsed is null)
            {
                continue;
            }

            var score = ScoreCandidate(parsed, schema);
            if (score < bestScore || (score == bestScore && fragment.Length <= bestLength))
            {
                continue;
            }

            best = parsed;
            bestScore = score;
            bestLength = fragment.Length;
        }

        return best;
    }

    private static bool TryParseNode(string content, out JsonNode? node)
    {
        try
        {
            node = JsonNode.Parse(content, documentOptions: TolerantJsonOptions);
            return node is not null;
        }
        catch (JsonException)
        {
            node = null;
            return false;
        }
    }

    private static int ScoreCandidate(JsonNode node, JsonObject? schema)
    {
        if (schema is null)
        {
            return 0;
        }

        var normalized = NormalizeEnvelope(node.DeepClone(), schema);
        if (!MatchesRootType(normalized, schema))
        {
            return -100;
        }

        var properties = schema["properties"] as JsonObject;
        if (properties?["rows"] is not null)
        {
            return normalized is JsonObject root && ContainsRows(root) ? 100 : 0;
        }

        if (schema["required"] is not JsonArray required || normalized is not JsonObject objectNode)
        {
            return 10;
        }

        var matches = 0;
        foreach (var requiredItem in required.OfType<JsonValue>())
        {
            if (
                requiredItem.TryGetValue<string>(out var propertyName)
                && TryGetProperty(objectNode, propertyName, out _)
            )
            {
                matches++;
            }
        }

        return 10 + matches;
    }

    private static IEnumerable<string> EnumerateJsonFragments(string content)
    {
        for (var start = 0; start < content.Length; start++)
        {
            if (content[start] is not ('{' or '['))
            {
                continue;
            }

            var fragment = TryExtractBalancedJson(content, start);
            if (!string.IsNullOrWhiteSpace(fragment))
            {
                yield return fragment;
            }
        }
    }

    private static string? TryExtractBalancedJson(string content, int start)
    {
        var closings = new Stack<char>();
        var inString = false;
        var escaped = false;

        for (var index = start; index < content.Length; index++)
        {
            var current = content[index];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (current == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '{')
            {
                closings.Push('}');
                continue;
            }

            if (current == '[')
            {
                closings.Push(']');
                continue;
            }

            if (current is not ('}' or ']'))
            {
                continue;
            }

            if (closings.Count == 0 || closings.Pop() != current)
            {
                return null;
            }

            if (closings.Count == 0)
            {
                return content[start..(index + 1)];
            }
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
                    nested = ParseResponseNode(text, schema);
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
                    var parsedRows = ParseResponseNode(text, schema);
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
