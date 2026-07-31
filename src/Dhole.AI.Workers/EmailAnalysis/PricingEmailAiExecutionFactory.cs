using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Dhole.AI.Application.Abstractions.Services;

namespace Dhole.AI.Worker.EmailAnalysis;

internal static class PricingEmailAiExecutionFactory
{
    private const int MaximumSourceCharactersPerStage = 3_500;
    private const int MaximumFocusedSourceCharacters = 2_200;
    private const int MaximumEmailContextCharacters = 700;
    private const int MaximumPreviousRows = 10;
    private const int MaximumPreviousIssues = 16;
    private const int MaximumCatalogItemsPerGroup = 5;
    private const int MaximumStages = 2;

    private static readonly string[] PricingKeywords =
    [
        "POL", "POE", "POD", "ORIGIN", "DESTINATION", "PORT", "PUERTO",
        "20GP", "40GP", "40HC", "45HC", "CONTAINER", "EQUIPO", "CARRIER",
        "NAVIERA", "FREIGHT", "FLETE", "USD", "EUR", "CRC", "VALID",
        "VIGENCIA", "TRANSIT", "FREE DAYS", "DIAS LIBRES", "AGENT", "AGENTE",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web
    )
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean" },
            "confidence": { "type": "number", "minimum": 0, "maximum": 100 },
            "rows": {
              "type": "array",
              "maxItems": 30,
              "items": {
                "type": "object",
                "additionalProperties": false,
                "properties": {
                  "pol": { "type": ["string", "null"] },
                  "poe": { "type": ["string", "null"] },
                  "pod": { "type": ["string", "null"] },
                  "containerType": { "type": ["string", "null"] },
                  "carrier": { "type": ["string", "null"] },
                  "agent": { "type": ["string", "null"] },
                  "commodity": { "type": ["string", "null"] },
                  "currency": { "type": "string", "minLength": 3, "maxLength": 3 },
                  "freeDays": { "type": ["integer", "null"], "minimum": 0 },
                  "transitDays": { "type": ["integer", "null"], "minimum": 0 },
                  "validFrom": { "type": ["string", "null"] },
                  "validTo": { "type": ["string", "null"] },
                  "oceanFreight": { "type": ["number", "null"] },
                  "originCharges": { "type": ["number", "null"] },
                  "destinationCharges": { "type": ["number", "null"] },
                  "surcharges": { "type": ["number", "null"] },
                  "totalCost": { "type": ["number", "null"] },
                  "totalSale": { "type": ["number", "null"] },
                  "profit": { "type": ["number", "null"] },
                  "margin": { "type": ["number", "null"] },
                  "spaceComment": { "type": ["string", "null"] },
                  "remarks": { "type": ["string", "null"] }
                },
                "required": [
                  "pol", "poe", "pod", "containerType", "carrier", "agent",
                  "commodity", "currency", "freeDays", "transitDays",
                  "validFrom", "validTo", "oceanFreight", "originCharges",
                  "destinationCharges", "surcharges", "totalCost", "totalSale",
                  "profit", "margin", "spaceComment", "remarks"
                ]
              }
            },
            "warnings": {
              "type": "array",
              "items": { "type": "string" }
            }
          },
          "required": ["success", "confidence", "rows", "warnings"]
        }
        """;

    public static IReadOnlyCollection<PreparedAiEmailExecution> CreateStages(
        DataExtractionAiEmailRequestResponse response,
        AiPricingEmailPayload payload,
        byte[]? imageBytes
    )
    {
        var isBodySource = payload.SourceType.Contains(
            "Body",
            StringComparison.OrdinalIgnoreCase
        );
        var emailContext = isBodySource
            ? null
            : LimitPreservingEdges(
                FirstNotEmpty(payload.BodyText, payload.BodyHtml),
                MaximumEmailContextCharacters
            );
        var stages = new List<StageDefinition>();
        var hasPreviousRows = payload.PreviousRows.Count > 0;

        if (imageBytes is { Length: > 0 })
        {
            stages.Add(new StageDefinition(
                "image-or-repair",
                LimitPreservingEdges(
                    payload.SourceContent,
                    MaximumFocusedSourceCharacters
                ),
                IncludePreviousExtraction: true,
                IncludeImage: true
            ));
        }
        else if (hasPreviousRows)
        {
            stages.Add(new StageDefinition(
                "repair-deterministic-draft",
                BuildFocusedSource(payload),
                IncludePreviousExtraction: true,
                IncludeImage: false
            ));

            var sourceFallback = SplitSourceIntoChunks(payload.SourceContent)
                .FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(sourceFallback))
            {
                stages.Add(new StageDefinition(
                    "focused-source-fallback",
                    sourceFallback,
                    IncludePreviousExtraction: false,
                    IncludeImage: false
                ));
            }
        }
        else
        {
            foreach (var chunk in SplitSourceIntoChunks(payload.SourceContent))
            {
                stages.Add(new StageDefinition(
                    "source-chunk",
                    chunk,
                    IncludePreviousExtraction: false,
                    IncludeImage: false
                ));
            }
        }

        if (stages.Count == 0)
        {
            stages.Add(new StageDefinition(
                "metadata-only",
                null,
                IncludePreviousExtraction: hasPreviousRows,
                IncludeImage: imageBytes is { Length: > 0 }
            ));
        }

        var selectedStages = stages.Take(MaximumStages).ToArray();
        return selectedStages
            .Select((stage, index) => CreateStage(
                response,
                payload,
                imageBytes,
                emailContext,
                stage,
                index + 1,
                selectedStages.Length
            ))
            .ToArray();
    }

    public static PreparedAiEmailExecution Create(
        DataExtractionAiEmailRequestResponse response,
        AiPricingEmailPayload payload,
        byte[]? imageBytes
    ) => CreateStages(response, payload, imageBytes).First();

    private static PreparedAiEmailExecution CreateStage(
        DataExtractionAiEmailRequestResponse response,
        AiPricingEmailPayload payload,
        byte[]? imageBytes,
        string? emailContext,
        StageDefinition stage,
        int stageNumber,
        int stageCount
    )
    {
        var prompt = JsonSerializer.Serialize(
            new
            {
                taskVersion = "fcl-email-v3-staged",
                stage = new
                {
                    name = stage.Name,
                    number = stageNumber,
                    count = stageCount,
                    instruction = stage.IncludePreviousExtraction
                        ? "Corrige únicamente el borrador determinístico con la evidencia mínima incluida."
                        : "Extrae únicamente las filas presentes en este fragmento; no completes datos de otros fragmentos.",
                },
                rules = new[]
                {
                    "Devuelve solo el JSON del esquema; no inventes valores.",
                    "POL es origen; Destination/Port of Discharge/Arrival/Gateway es POE.",
                    "POD solo ante POD/Place of Delivery/Final Destination explícito; no copies POE.",
                    "Crea una fila por ruta y contenedor; separa equipos agrupados.",
                    "Usa exactamente nombres canónicos inequívocos de catalogHints.",
                    "agent solo si la tarifa lo indica; no lo deduzcas del remitente o la firma.",
                    "currency es obligatoria: USD salvo otra moneda explícita.",
                    "No expliques el procedimiento ni repitas el contenido de entrada.",
                },
                emailMessageId = payload.EmailMessageId,
                emailAttachmentId = payload.EmailAttachmentId,
                subject = payload.Subject,
                sourceType = payload.SourceType,
                sourceName = payload.SourceName,
                sourceContentType = payload.SourceContentType,
                emailContext,
                sourceContent = stage.SourceContent,
                sourceImage = stage.IncludeImage && imageBytes is { Length: > 0 }
                    ? new
                    {
                        attached = true,
                        mimeType = response.Image.ContentType
                            ?? payload.SourceImageMimeType,
                    }
                    : null,
                catalogHints = payload.CatalogHints.Select(group => new
                {
                    group = group.GroupSlug,
                    items = group.Items
                        .Take(MaximumCatalogItemsPerGroup)
                        .Select(item => new
                        {
                            item.Name,
                            item.Code,
                        }),
                }),
                previousExtraction = stage.IncludePreviousExtraction
                    ? new
                    {
                        errorCode = payload.PreviousErrorCode,
                        errorMessage = payload.PreviousErrorMessage,
                        confidence = payload.PreviousConfidence,
                        rows = payload.PreviousRows.Take(MaximumPreviousRows),
                        issues = payload.PreviousIssues
                            .Take(MaximumPreviousIssues)
                            .Select(issue => new
                            {
                                issue.Code,
                                issue.ColumnName,
                                issue.RawValue,
                                issue.IsBlocking,
                            }),
                    }
                    : null,
            },
            JsonOptions
        );
        var stageHash = ComputeSha256(
            $"{response.RequestHash}|{stageNumber}|{stage.Name}|{prompt}"
        );

        return new PreparedAiEmailExecution(
            response.ProfileKey,
            prompt,
            JsonSchema,
            response.CorrelationId,
            stageHash,
            stage.IncludeImage && imageBytes is { Length: > 0 }
                ? response.Image.ContentType ?? payload.SourceImageMimeType
                : null,
            stage.IncludeImage ? imageBytes : null,
            stage.Name,
            stageNumber,
            stageCount
        );
    }

    public static ExecuteAiStructuredInput ToApplicationInput(
        PreparedAiEmailExecution execution
    )
    {
        IReadOnlyCollection<AiExecutionImageInput>? images =
            execution.ImageBytes is null
            || string.IsNullOrWhiteSpace(execution.ImageMimeType)
                ? null
                :
                [
                    new AiExecutionImageInput(
                        execution.ImageMimeType,
                        Convert.ToBase64String(execution.ImageBytes)
                    ),
                ];

        return new ExecuteAiStructuredInput(
            execution.ProfileKey,
            [new AiExecutionMessageInput("user", execution.PromptJson, images)],
            null,
            execution.JsonSchema,
            execution.CorrelationId,
            execution.RequestHash,
            null
        );
    }

    public static ParsedAiPricingEmailResult Parse(string json)
    {
        var response = JsonSerializer.Deserialize<AiModelResponse>(
            json,
            JsonOptions
        ) ?? throw new AiEmailJobException(
            "AI.InvalidPricingEmailResponse",
            "AI devolvió un JSON vacío para el correo.",
            isTransient: true
        );
        var rows = response.Rows
            .Where(HasPricingData)
            .Select(WithDefaultCurrency)
            .Take(30)
            .ToArray();
        if (!response.Success || rows.Length == 0)
        {
            throw new AiEmailJobException(
                "AI.NoPricingRows",
                response.Warnings.FirstOrDefault()
                    ?? "AI no encontró filas de tarifas utilizables.",
                isTransient: true
            );
        }

        var confidence = response.Confidence is > 0m and <= 1m
            ? response.Confidence * 100m
            : response.Confidence;
        return new ParsedAiPricingEmailResult(
            Math.Clamp(confidence, 0m, 100m),
            rows,
            response.Warnings
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
        );
    }

    public static ParsedAiPricingEmailResult Merge(
        IReadOnlyCollection<ParsedAiPricingEmailResult> results
    )
    {
        if (results.Count == 0)
        {
            throw new AiEmailJobException(
                "AI.NoPricingRows",
                "Ninguna etapa de AI produjo filas utilizables.",
                isTransient: true
            );
        }

        var rows = results
            .SelectMany(item => item.Rows)
            .GroupBy(CreateRowKey, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(100)
            .ToArray();
        var warnings = results
            .SelectMany(item => item.Warnings)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ParsedAiPricingEmailResult(
            results.Max(item => item.Confidence),
            rows,
            warnings
        );
    }

    private static string BuildFocusedSource(AiPricingEmailPayload payload)
    {
        var issueTerms = payload.PreviousIssues
            .SelectMany(issue => new[] { issue.RawValue, issue.ColumnName })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Take(20)
            .ToArray();
        var selectedLines = payload.SourceContent
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(line =>
                PricingKeywords.Any(keyword =>
                    line.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                )
                || issueTerms.Any(term =>
                    line.Contains(term, StringComparison.OrdinalIgnoreCase)
                )
            )
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var focused = selectedLines.Length > 0
            ? string.Join('\n', selectedLines)
            : payload.SourceContent;

        return LimitPreservingEdges(
            focused,
            MaximumFocusedSourceCharacters
        ) ?? string.Empty;
    }

    private static IReadOnlyCollection<string> SplitSourceIntoChunks(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return Array.Empty<string>();
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var chunks = new List<string>();
        var builder = new StringBuilder();

        foreach (var line in lines)
        {
            if (
                builder.Length > 0
                && builder.Length + line.Length + 1 > MaximumSourceCharactersPerStage
            )
            {
                chunks.Add(builder.ToString());
                builder.Clear();
                if (chunks.Count >= MaximumStages)
                {
                    break;
                }
            }

            if (line.Length > MaximumSourceCharactersPerStage)
            {
                builder.AppendLine(line[..MaximumSourceCharactersPerStage]);
            }
            else
            {
                builder.AppendLine(line);
            }
        }

        if (builder.Length > 0 && chunks.Count < MaximumStages)
        {
            chunks.Add(builder.ToString());
        }

        if (chunks.Count == 0)
        {
            var limited = LimitPreservingEdges(
                value,
                MaximumSourceCharactersPerStage
            );
            if (!string.IsNullOrWhiteSpace(limited))
            {
                chunks.Add(limited);
            }
        }

        return chunks;
    }

    private static bool HasPricingData(AiPricingEmailResultRow row)
    {
        return !string.IsNullOrWhiteSpace(row.Pol)
            || !string.IsNullOrWhiteSpace(row.Poe)
            || !string.IsNullOrWhiteSpace(row.Pod)
            || !string.IsNullOrWhiteSpace(row.ContainerType)
            || !string.IsNullOrWhiteSpace(row.Carrier)
            || row.OceanFreight.HasValue
            || row.TotalCost.HasValue
            || row.TotalSale.HasValue;
    }

    private static AiPricingEmailResultRow WithDefaultCurrency(
        AiPricingEmailResultRow row
    )
    {
        return string.IsNullOrWhiteSpace(row.Currency)
            ? row with { Currency = "USD" }
            : row;
    }

    private static string CreateRowKey(AiPricingEmailResultRow row)
    {
        return string.Join("|",
            row.Pol,
            row.Poe,
            row.Pod,
            row.ContainerType,
            row.Carrier,
            row.Agent,
            row.Currency,
            row.OceanFreight?.ToString(System.Globalization.CultureInfo.InvariantCulture),
            row.ValidFrom?.ToString("yyyy-MM-dd"),
            row.ValidTo?.ToString("yyyy-MM-dd")
        );
    }

    private static string? LimitPreservingEdges(
        string? value,
        int maximumCharacters
    )
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        if (normalized.Length <= maximumCharacters)
        {
            return normalized;
        }

        const string marker = "\n[CONTENIDO INTERMEDIO OMITIDO PARA AI LOCAL]\n";
        var available = maximumCharacters - marker.Length;
        var headLength = available * 3 / 4;
        var tailLength = available - headLength;
        return normalized[..headLength] + marker + normalized[^tailLength..];
    }

    private static string? FirstNotEmpty(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))
            ?.Trim();
    }

    private static string ComputeSha256(string value)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)))
            .ToLowerInvariant();
    }

    private sealed record StageDefinition(
        string Name,
        string? SourceContent,
        bool IncludePreviousExtraction,
        bool IncludeImage
    );

    private sealed record AiModelResponse(
        bool Success,
        decimal Confidence,
        IReadOnlyCollection<AiPricingEmailResultRow> Rows,
        IReadOnlyCollection<string> Warnings
    );
}
