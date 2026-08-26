using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Dhole.AI.Application.Abstractions.Services;

namespace Dhole.AI.Worker.EmailAnalysis;

internal static class PricingEmailAiExecutionFactory
{
    private const int MaximumSourceCharactersPerStage = 12_000;
    private const int MaximumFocusedSourceCharacters = 10_000;
    private const int MaximumEmailContextCharacters = 4_000;
    private const int MaximumPreviousRows = 10;
    private const int MaximumPreviousIssues = 16;
    private const int MaximumCatalogItemsPerGroup = 20;
    private const int MaximumStages = 2;

    private static readonly string[] PricingKeywords =
    [
        "POL", "POE", "POD", "ORIGIN", "DESTINATION", "PORT", "PUERTO",
        "20GP", "40GP", "40HC", "45HC", "CONTAINER", "EQUIPO", "CARRIER",
        "NAVIERA", "FREIGHT", "FLETE", "USD", "EUR", "CRC", "VALID",
        "VIGENCIA", "TRANSIT", "FREE DAYS", "DIAS LIBRES", "AGENT", "AGENTE",
        "COMM", "COMMODITY", "NAC", "ARB", "SUBJECT TO", "BELOW THE DETAILS", "SPACE",
    ];

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();


    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        };
        options.Converters.Add(new AiPricingEmailResultRowJsonConverter());
        return options;
    }

    public const string JsonSchema = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": { "type": "boolean" },
            "confidence": { "type": "number", "minimum": 0, "maximum": 100 },
            "rows": {
              "type": "array",
              "maxItems": 100,
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
                  "oceanFreight": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "originCharges": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "destinationCharges": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "surcharges": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "totalCost": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "totalSale": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "profit": { "type": ["number", "null"], "minimum": -1000000000, "maximum": 1000000000 },
                  "margin": { "type": ["number", "null"], "minimum": -100000, "maximum": 100000 },
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
        var focusedSourceContent = SelectNewestPricingSection(payload.SourceContent);
        var emailContext = isBodySource
            ? null
            : LimitPreservingEdges(
                SelectNewestPricingSection(
                    FirstNotEmpty(payload.BodyText, payload.BodyHtml)
                ),
                MaximumEmailContextCharacters
            );
        var stages = new List<StageDefinition>();
        var hasPreviousRows = payload.PreviousRows.Count > 0;

        if (imageBytes is { Length: > 0 })
        {
            stages.Add(new StageDefinition(
                "image-or-repair",
                LimitPreservingEdges(
                    focusedSourceContent,
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
                BuildFocusedSource(payload, focusedSourceContent),
                IncludePreviousExtraction: true,
                IncludeImage: false
            ));

            var sourceFallback = SplitSourceIntoChunks(focusedSourceContent)
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
            foreach (var chunk in SplitSourceIntoChunks(focusedSourceContent))
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
                taskVersion = "fcl-email-v11-body-multi-table",
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
                    "El contenido fue enfocado al mensaje tarifario más reciente. Ignora cualquier tarifa histórica, firma o conversación citada que todavía aparezca.",
                    "Si todavía aparece una cadena de respuestas o reenviados, la primera sección visible con una tarifa FCL completa es la vigente. Nunca prefieras una sección posterior solo porque tenga más filas, montos o detalle; las secciones posteriores pertenecen al historial.",
                    "POL es origen; Destination/Port of Discharge/Arrival/Gateway es POE.",
                    "En tarifas marítimas, POD normalmente significa Port of Discharge y se guarda en poe; usa pod solo para Place of Delivery o Final Destination explícito.",
                    "Devuelve filas compactas: agrupa con / los POL o puertos de descarga que compartan exactamente carrier, equipo, mercancía, vigencia, flete y recargos; DataExtraction los expandirá después.",
                    "Separa filas cuando cambie carrier, containerType, commodity, oceanFreight u originCharges; nunca unas varias navieras en una fila.",
                    "Extrae todas las tablas tarifarias del mensaje actual; una segunda matriz del mismo correo no es historial y no debe omitirse.",
                    "El cuerpo BodyText/BodyHtml es una fuente tarifaria completa aunque no exista adjunto; no rechaces un correo por no tener PDF, Excel o imagen.",
                    "Si una tabla HTML fue convertida a texto vertical, reconstruye sus columnas usando el orden de encabezados y valores repetidos. Un encabezado PORT OF DESTINATION inicia un bloque independiente y cada bloque debe producir sus propias filas.",
                    "Reconoce equivalencias de equipo 20-DV/20DV/20FT-DV como 20DV, 40-DV/40DV/40FT-DV como 40DV y 40-HC/40HC como 40HC; nunca mezcles los montos entre columnas.",
                    "Si aparece ASIA BASE PORTS y el mismo correo enumera Base Ports, usa esa lista explícita de puertos como POL compacto separado por / en lugar de devolver literalmente ASIA BASE PORTS.",
                    "Cuando el proveedor publica OCEAN FREIGHT y TOTAL ALL IN, oceanFreight conserva solo el OCEAN FREIGHT y totalCost conserva exactamente el TOTAL ALL IN publicado. No conviertas totalCost en totalSale. Los cargos por BL pueden quedar en remarks aunque estén incluidos en el total publicado.",
                    "La vigencia puede estar en subject o body, por ejemplo VALIDO DEL 01 AL 06 DE SEPTIEMBRE 2026; aplícala a todas las filas del bloque tarifario correspondiente.",
                    "En columnas de monto por equipo, 20' corresponde a 20DV/20GP. Si el encabezado es 40'/40HC y comparte un monto, devuelve una fila 40DV/40GP y otra 40HC con el mismo oceanFreight.",
                    "Effective ETD con fecha representa una vigencia de un solo día: usa la misma fecha en validFrom y validTo. Si dice OMIT/OMITTED/NO SAILING/CANCELLED, omite esa ruta de rows y agrega warning.",
                    "Cuando montos y carriers aparecen en listas paralelas, asócialos por posición: USD6300/6400 con MSC/ONE significa MSC=6300 y ONE=6400, salvo evidencia explícita contraria.",
                    "En una tabla o tarifa marítima, POD suele significar Port of Discharge y debe guardarse en poe; pod se reserva para Place of Delivery o Final Destination explícito.",
                    "Si el correo dice Below the details of ONE NAC, las restricciones COMM de A/B/C son solo de ONE. MSC puede conservar una fila general con las rutas compartidas, sin copiar esas mercancías y respetando exclusiones explícitas.",
                    "Un POL con arbitrario distinto debe ir en una fila compacta separada: Tianjin (+ arb USD100) implica originCharges=100; no lo sumes a oceanFreight.",
                    "Suma en surcharges solo cargos por contenedor: ISPS 15/cntr + P/S 50/cntr = 65. Conserva cargos por BL únicamente en remarks.",
                    "Los recargos condicionales por peso/equipo se conservan en remarks y no se suman automáticamente; por ejemplo ONE overweight surcharge 18-21 tons USD 200/20'.",
                    "Si aparece General Cargo después de las tablas, úsalo como commodity de esas tarifas salvo mercancía explícita distinta. Conserva Subject to DTHC/local charges en remarks.",
                    "Para cualquier importe desconocido usa null. Nunca uses números gigantes, infinitos, exponentes extremos ni valores centinela; surcharges debe ser un único número decimal razonable.",
                    "Conserva en spaceComment excepciones de espacio como except TIANJIN/XIAMEN y aplícalas solo a la oferta correspondiente.",
                    "Conserva en spaceComment notas generales de disponibilidad como space is tight, rollovers o confirmación de espacio caso por caso.",
                    "Una proyección futura de aumento se conserva en remarks como nota comercial y nunca se suma a oceanFreight, surcharges ni totales actuales.",
                    "Usa exactamente nombres canónicos inequívocos de catalogHints.",
                    "Para agent revisa primero subject y luego emailContext; usa solo una coincidencia inequívoca de catalogHints y tolera una errata mínima.",
                    "No deduzcas agent únicamente por la dirección del remitente.",
                    "Para el patrón narrativo MSC/ONE NAC con tarifa USDx/y y equipo omitido, containerType es obligatorio y debe ser 40HC; no lo devuelvas como null.",
                    "Resuelve rangos sin año con processingDateUtc y devuelve fechas YYYY-MM-DD.",
                    "currency es obligatoria: USD salvo otra moneda explícita.",
                    "No expliques el procedimiento ni repitas el contenido de entrada.",
                },
                emailMessageId = payload.EmailMessageId,
                emailAttachmentId = payload.EmailAttachmentId,
                subject = payload.Subject,
                sourceType = payload.SourceType,
                sourceName = payload.SourceName,
                sourceContentType = payload.SourceContentType,
                processingDateUtc = DateTime.UtcNow.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
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
        AiModelResponse response;
        try
        {
            response = JsonSerializer.Deserialize<AiModelResponse>(json, JsonOptions)
                ?? throw new AiEmailJobException(
                    "AI.InvalidPricingEmailResponse",
                    "AI devolvió un JSON vacío para el correo.",
                    isTransient: true
                );
        }
        catch (AiEmailJobException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new AiEmailJobException(
                "AI.InvalidPricingEmailResponse",
                "AI devolvió un JSON que no pudo normalizarse. El trabajo puede reintentarse con otro modelo.",
                isTransient: true,
                exception
            );
        }

        var warnings = response.Warnings ?? [];
        var rows = (response.Rows ?? [])
            .Where(HasPricingData)
            .Select(WithDefaultCurrency)
            .Take(100)
            .ToArray();
        if (!response.Success || rows.Length == 0)
        {
            throw new AiEmailJobException(
                "AI.NoPricingRows",
                warnings.FirstOrDefault()
                    ?? "AI no encontró filas de tarifas utilizables.",
                isTransient: true
            );
        }

        var confidenceValue = ReadConfidence(response.Confidence);
        var confidence = confidenceValue is > 0m and <= 1m
            ? confidenceValue * 100m
            : confidenceValue;
        return new ParsedAiPricingEmailResult(
            Math.Clamp(confidence, 0m, 100m),
            rows,
            warnings
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

    public static ParsedAiPricingEmailResult NormalizeForSource(
        ParsedAiPricingEmailResult result,
        AiPricingEmailPayload payload
    )
    {
        var source = SelectNewestPricingSection(
            FirstNotEmpty(payload.SourceContent, payload.BodyText, payload.BodyHtml)
        );

        // Local models can understand this WWL contract but still mix the paired
        // amounts/carriers, copy ONE commodity restrictions to MSC, omit 40HC or
        // resolve 8-14/Aug against the processing day. The source itself is fully
        // deterministic, so rebuild the compact matrix before applying generic
        // semantic normalization. This also makes the result independent of the
        // exact Ollama model selected by the profile.
        var reconstructedNac = TryReconstructWwlPairedNarrativeNac(
            result,
            payload,
            source
        );
        if (reconstructedNac is not null)
        {
            return reconstructedNac;
        }

        var isMaritimeTariff = IsMaritimeTariffSource(source);
        var isNarrativeNac = IsNarrativeNacSource(source);
        var inferredContainerType = isNarrativeNac
            ? InferContainerTypeFromSource(source) ?? "40HC"
            : null;
        var inferredContainer = false;
        var promotedPod = false;

        var rows = result.Rows
            .Select(row =>
            {
                var poe = row.Poe;
                var pod = row.Pod;
                if (isMaritimeTariff && !string.IsNullOrWhiteSpace(pod))
                {
                    // Regla Dhole: POD en una tarifa marítima significa Port of
                    // Discharge y siempre se persiste como POE. POD queda reservado
                    // para Place of Delivery/Final Destination, que este flujo no usa.
                    poe = pod.Trim();
                    pod = null;
                    promotedPod = true;
                }

                var containerType = row.ContainerType;
                if (
                    string.IsNullOrWhiteSpace(containerType)
                    && !string.IsNullOrWhiteSpace(inferredContainerType)
                )
                {
                    containerType = inferredContainerType;
                    inferredContainer = true;
                }

                return row with
                {
                    Poe = string.IsNullOrWhiteSpace(poe) ? null : poe.Trim(),
                    Pod = string.IsNullOrWhiteSpace(pod) ? null : pod.Trim(),
                    ContainerType = string.IsNullOrWhiteSpace(containerType)
                        ? null
                        : containerType.Trim(),
                };
            })
            .ToArray();

        var warnings = result.Warnings.ToList();
        if (inferredContainer)
        {
            warnings.Add(
                $"containerType inferido como {inferredContainerType} para la oferta narrativa MSC/ONE NAC."
            );
        }

        if (promotedPod)
        {
            warnings.Add(
                "POD marítimo normalizado como POE según la regla de importación de tarifas."
            );
        }

        return result with
        {
            Rows = rows,
            Warnings = warnings
                .Where(item => !string.IsNullOrWhiteSpace(item))
                .Select(item => item.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray(),
        };
    }

    private static ParsedAiPricingEmailResult? TryReconstructWwlPairedNarrativeNac(
        ParsedAiPricingEmailResult result,
        AiPricingEmailPayload payload,
        string source
    )
    {
        if (string.IsNullOrWhiteSpace(source))
        {
            return null;
        }

        if (
            !source.Contains("Below the details of ONE NAC", StringComparison.OrdinalIgnoreCase)
            && !source.Contains("ONE NAC must match COMM", StringComparison.OrdinalIgnoreCase)
        )
        {
            return null;
        }

        var cleanSource = Regex.Replace(source, @"[\*\u00A0]", " ");
        cleanSource = Regex.Replace(cleanSource, @"[ \t]+", " ");
        var offerMatch = Regex.Match(
            cleanSource,
            @"(?is)\b(?:pls|please)\s+consider\s+(?:the\s+)?rate\s+"
                + @"(?<ratePart>.+?)\s*,?\s*valid\s+"
                + @"(?<fromDay>\d{1,2})\s*-\s*(?<toDay>\d{1,2})\s*/\s*(?<month>[A-Za-z]{3,9})\s+"
                + @"Carrier\s+(?<carrierPart>.+?)\s+NAC\b"
        );
        if (!offerMatch.Success)
        {
            return null;
        }

        var carriers = Regex.Split(
                offerMatch.Groups["carrierPart"].Value,
                @"\s*(?:/|,|\band\b|\by\b)\s*",
                RegexOptions.IgnoreCase
            )
            .Select(NormalizeCarrierName)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .ToArray();
        var amounts = Regex.Matches(
                offerMatch.Groups["ratePart"].Value,
                @"(?:(?:USD|US\$|\$)\s*)?(?<amount>\d[\d,]*(?:\.\d+)?)",
                RegexOptions.IgnoreCase
            )
            .Select(match => ParseReasonableDecimal(match.Groups["amount"].Value))
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        if (carriers.Length < 2 || carriers.Length != amounts.Length)
        {
            return null;
        }

        var carrierRates = carriers
            .Select((carrier, index) => new NarrativeCarrierRate(carrier, amounts[index]))
            .ToArray();
        if (
            !carrierRates.Any(item => item.Carrier.Equals("MSC", StringComparison.OrdinalIgnoreCase))
            || !carrierRates.Any(item => item.Carrier.Equals("ONE", StringComparison.OrdinalIgnoreCase))
        )
        {
            return null;
        }

        var month = ParseMonthNumber(offerMatch.Groups["month"].Value);
        if (
            month is null
            || !int.TryParse(offerMatch.Groups["fromDay"].Value, out var fromDay)
            || !int.TryParse(offerMatch.Groups["toDay"].Value, out var toDay)
        )
        {
            return null;
        }

        var processingDate = result.Rows
            .Select(row => row.ValidFrom ?? row.ValidTo)
            .FirstOrDefault(value => value.HasValue)
            ?.Date
            ?? DateTime.UtcNow.Date;
        var year = ResolveValidityYear(processingDate, month.Value);
        if (
            !TryCreateSourceDate(year, month.Value, fromDay, out var validFrom)
            || !TryCreateSourceDate(year, month.Value, toDay, out var validTo)
        )
        {
            return null;
        }

        var groups = ParseNarrativeGroups(cleanSource);
        if (groups.Count == 0)
        {
            return null;
        }

        var freeDaysMatch = Regex.Match(
            cleanSource,
            @"\b(?<days>\d{1,3})\s*days?\s+free\b",
            RegexOptions.IgnoreCase
        );
        var freeDays = freeDaysMatch.Success
            && int.TryParse(freeDaysMatch.Groups["days"].Value, out var parsedFreeDays)
                ? parsedFreeDays
                : (int?)null;
        var surcharges = ParsePerContainerSurcharges(cleanSource);
        var remarks = ParsePerBillRemarks(cleanSource);
        var exclusions = ParseExcludedOrigins(cleanSource);
        var agent = result.Rows
            .Select(row => row.Agent)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
        if (string.IsNullOrWhiteSpace(agent)
            && payload.Subject.Contains("WWL", StringComparison.OrdinalIgnoreCase))
        {
            agent = "WWL";
        }

        var rows = new List<AiPricingEmailResultRow>();
        foreach (var carrierRate in carrierRates)
        {
            if (carrierRate.Carrier.Equals("MSC", StringComparison.OrdinalIgnoreCase))
            {
                var origins = groups
                    .SelectMany(group => group.Origins)
                    .Select(item => item.Port)
                    .Where(port => !exclusions.Contains(port, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                var portsOfDischarge = groups
                    .SelectMany(group => group.PortsOfDischarge)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray();

                if (origins.Length > 0 && portsOfDischarge.Length > 0)
                {
                    rows.Add(CreateNarrativeRow(
                        string.Join('/', origins),
                        string.Join('/', portsOfDischarge),
                        carrierRate.Carrier,
                        agent,
                        commodity: null,
                        carrierRate.Amount,
                        originCharges: null,
                        freeDays,
                        validFrom,
                        validTo,
                        surcharges,
                        exclusions.Count == 0
                            ? null
                            : $"except {string.Join('/', exclusions)}",
                        JoinNarrativeRemarks(
                            remarks,
                            "Las restricciones COMM A/B/C aplican únicamente a ONE NAC."
                        )
                    ));
                }

                continue;
            }

            if (carrierRate.Carrier.Equals("ONE", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var group in groups)
                {
                    foreach (var chargeGroup in group.Origins.GroupBy(item => item.OriginCharge))
                    {
                        rows.Add(CreateNarrativeRow(
                            string.Join('/', chargeGroup.Select(item => item.Port)),
                            string.Join('/', group.PortsOfDischarge),
                            carrierRate.Carrier,
                            agent,
                            group.Commodity,
                            carrierRate.Amount,
                            chargeGroup.Key,
                            freeDays,
                            validFrom,
                            validTo,
                            surcharges,
                            exclusions.Count == 0
                                ? null
                                : $"except {string.Join('/', exclusions)}",
                            JoinNarrativeRemarks(
                                remarks,
                                chargeGroup.Key.HasValue
                                    ? $"Arbitrario de origen: USD {chargeGroup.Key.Value.ToString(CultureInfo.InvariantCulture)} por contenedor."
                                    : null
                            )
                        ));
                    }
                }
            }
        }

        if (rows.Count == 0)
        {
            return null;
        }

        var warnings = result.Warnings
            .Append(
                "Oferta WWL MSC/ONE NAC reconstruida desde la fuente: MSC=primer monto, ONE=segundo monto, equipo=40HC y vigencia tomada del rango explícito."
            )
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new ParsedAiPricingEmailResult(
            100m,
            rows,
            warnings
        );
    }

    private static AiPricingEmailResultRow CreateNarrativeRow(
        string pol,
        string poe,
        string carrier,
        string? agent,
        string? commodity,
        decimal oceanFreight,
        decimal? originCharges,
        int? freeDays,
        DateTime validFrom,
        DateTime validTo,
        decimal? surcharges,
        string? spaceComment,
        string? remarks
    ) => new(
        pol,
        poe,
        null,
        "40HC",
        carrier,
        agent,
        commodity,
        "USD",
        freeDays,
        null,
        validFrom,
        validTo,
        oceanFreight,
        originCharges,
        null,
        surcharges,
        null,
        null,
        null,
        null,
        spaceComment,
        remarks
    );

    private static IReadOnlyList<NarrativeGroup> ParseNarrativeGroups(string source)
    {
        var lines = source
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => line.Trim().TrimStart('>', '|', '-', '*').Trim().TrimEnd('*'))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        var result = new List<NarrativeGroup>();
        string? pol = null;
        string? poe = null;
        string? commodity = null;

        void Flush()
        {
            if (string.IsNullOrWhiteSpace(pol) || string.IsNullOrWhiteSpace(poe))
            {
                pol = null;
                poe = null;
                commodity = null;
                return;
            }

            var origins = SplitRouteValue(pol)
                .Select(ParseNarrativeOrigin)
                .Where(item => !string.IsNullOrWhiteSpace(item.Port))
                .DistinctBy(
                    item => $"{item.Port}|{item.OriginCharge?.ToString(CultureInfo.InvariantCulture)}",
                    StringComparer.OrdinalIgnoreCase
                )
                .ToArray();
            var ports = SplitRouteValue(poe)
                .Select(RemoveArbitraryCharge)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (origins.Length > 0 && ports.Length > 0)
            {
                result.Add(new NarrativeGroup(origins, ports, CleanNarrativeValue(commodity)));
            }

            pol = null;
            poe = null;
            commodity = null;
        }

        foreach (var line in lines)
        {
            if (Regex.IsMatch(line, @"^[A-Z]\)$", RegexOptions.IgnoreCase))
            {
                Flush();
                continue;
            }

            var pair = Regex.Match(
                line,
                @"^(?<key>POL|POD|POE|COMM(?:ODITY)?)\s*:\s*(?<value>.+)$",
                RegexOptions.IgnoreCase
            );
            if (!pair.Success)
            {
                continue;
            }

            var key = pair.Groups["key"].Value.ToUpperInvariant();
            var value = pair.Groups["value"].Value.Trim().TrimEnd('*').Trim();
            switch (key)
            {
                case "POL":
                    if (!string.IsNullOrWhiteSpace(pol) && !string.IsNullOrWhiteSpace(poe))
                    {
                        Flush();
                    }
                    pol = value;
                    break;
                case "POD":
                case "POE":
                    poe = value;
                    break;
                default:
                    commodity = value;
                    break;
            }
        }

        Flush();
        return result;
    }

    private static IReadOnlyList<string> SplitRouteValue(string value) => value
        .Trim()
        // Keep parentheses because the last POL can carry an arbitrary charge,
        // e.g. Chongqing(+arb USD850). Trimming ')' here loses that charge.
        .Trim('.', ';', ',')
        .Split(['/', ';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .Select(item => item.Trim())
        .Where(item => !string.IsNullOrWhiteSpace(item))
        .ToArray();

    private static NarrativeOrigin ParseNarrativeOrigin(string value)
    {
        var chargeMatch = Regex.Match(
            value,
            @"\(\s*\+?\s*arb(?:itrary)?\s+(?:USD|US\$|\$)\s*(?<amount>\d[\d,]*(?:\.\d+)?)\s*\)",
            RegexOptions.IgnoreCase
        );
        var charge = chargeMatch.Success
            ? ParseReasonableDecimal(chargeMatch.Groups["amount"].Value)
            : null;
        return new NarrativeOrigin(RemoveArbitraryCharge(value), charge);
    }

    private static string RemoveArbitraryCharge(string value) => Regex.Replace(
        value,
        @"\s*\(\s*\+?\s*arb(?:itrary)?\s+(?:USD|US\$|\$)\s*\d[\d,]*(?:\.\d+)?\s*\)\s*",
        string.Empty,
        RegexOptions.IgnoreCase
    ).Trim();

    private static string? CleanNarrativeValue(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Regex.Replace(value.Trim().TrimEnd('*'), @"[ \t]+", " ");
    }

    private static string NormalizeCarrierName(string value)
    {
        var clean = Regex.Replace(value.Trim(), @"\b(?:NAC|FAK|BASKET)\b", string.Empty, RegexOptions.IgnoreCase)
            .Trim()
            .ToUpperInvariant();
        return clean switch
        {
            "MSK" or "MAERSK" => "MAERSK",
            "CMA" or "CMA CGM" => "CMA CGM",
            "HPL" => "HAPAG-LLOYD",
            _ => clean,
        };
    }

    private static decimal? ParseReasonableDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Replace(",", string.Empty, StringComparison.Ordinal).Trim();
        return decimal.TryParse(
            normalized,
            NumberStyles.Number | NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var parsed
        ) && parsed is >= -1_000_000_000m and <= 1_000_000_000m
            ? parsed
            : null;
    }

    private static int? ParseMonthNumber(string value)
    {
        var normalized = value.Trim().ToLowerInvariant();
        return normalized switch
        {
            "jan" or "january" or "ene" or "enero" => 1,
            "feb" or "february" or "febrero" => 2,
            "mar" or "march" or "marzo" => 3,
            "apr" or "april" or "abr" or "abril" => 4,
            "may" or "mayo" => 5,
            "jun" or "june" or "junio" => 6,
            "jul" or "july" or "julio" => 7,
            "aug" or "august" or "ago" or "agosto" => 8,
            "sep" or "sept" or "september" or "septiembre" => 9,
            "oct" or "october" or "octubre" => 10,
            "nov" or "november" or "noviembre" => 11,
            "dec" or "december" or "dic" or "diciembre" => 12,
            _ => null,
        };
    }

    private static int ResolveValidityYear(DateTime processingDate, int month)
    {
        if (processingDate.Month >= 10 && month <= 2)
        {
            return processingDate.Year + 1;
        }

        if (processingDate.Month <= 2 && month >= 10)
        {
            return processingDate.Year - 1;
        }

        return processingDate.Year;
    }

    private static bool TryCreateSourceDate(
        int year,
        int month,
        int day,
        out DateTime value
    )
    {
        try
        {
            // Use noon UTC for commercial date-only values. Midnight UTC was
            // displayed as the previous day in Costa Rica, while noon preserves
            // the intended calendar date across the supported time zones.
            value = new DateTime(year, month, day, 12, 0, 0, DateTimeKind.Utc);
            return true;
        }
        catch (ArgumentOutOfRangeException)
        {
            value = default;
            return false;
        }
    }

    private static decimal? ParsePerContainerSurcharges(string source)
    {
        decimal total = 0m;
        var found = false;
        foreach (Match match in Regex.Matches(
            source,
            @"(?:USD|US\$|\$)\s*(?<amount>\d[\d,]*(?:\.\d+)?)\s*/\s*(?:cntr|container)\b",
            RegexOptions.IgnoreCase
        ))
        {
            var amount = ParseReasonableDecimal(match.Groups["amount"].Value);
            if (!amount.HasValue)
            {
                continue;
            }

            total += amount.Value;
            found = true;
        }

        return found ? total : null;
    }

    private static string? ParsePerBillRemarks(string source)
    {
        var match = Regex.Match(
            source,
            @"(?<label>MBL\s+RLS[^\r\n$]{0,80})\s*(?:USD|US\$|\$)?\s*(?<amount>\d[\d,]*(?:\.\d+)?)\s*/\s*(?:BL|BILL)\b",
            RegexOptions.IgnoreCase
        );
        if (!match.Success)
        {
            return null;
        }

        return $"MBL RLS at destination: USD {match.Groups["amount"].Value.Replace(",", string.Empty, StringComparison.Ordinal)}/BL.";
    }

    private static IReadOnlyList<string> ParseExcludedOrigins(string source)
    {
        var match = Regex.Match(
            source,
            @"\bexcept\s+(?<value>[^)\r\n]+)",
            RegexOptions.IgnoreCase
        );
        return match.Success
            ? SplitRouteValue(match.Groups["value"].Value)
                .Select(RemoveArbitraryCharge)
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
    }

    private static string? JoinNarrativeRemarks(params string?[] values)
    {
        var clean = values
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim().TrimEnd('.') + ".")
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        return clean.Length == 0 ? null : string.Join(' ', clean);
    }

    private static bool IsMaritimeTariffSource(string? source)
    {
        return !string.IsNullOrWhiteSpace(source)
            && Regex.IsMatch(source, @"\bPOL\s*:\s*\S+", RegexOptions.IgnoreCase)
            && Regex.IsMatch(source, @"\bPOD\s*:\s*\S+", RegexOptions.IgnoreCase);
    }

    private static bool IsNarrativeNacSource(string? source)
    {
        return IsMaritimeTariffSource(source)
            && source!.Contains("NAC", StringComparison.OrdinalIgnoreCase)
            && Regex.IsMatch(
                source,
                @"\b(?:pls|please)\s+consider\s+(?:the\s+)?rate\b",
                RegexOptions.IgnoreCase
            );
    }

    private static string? InferContainerTypeFromSource(string source)
    {
        var match = Regex.Match(
            source,
            @"\b(?<size>20|40|45)\s*['’]?\s*(?<type>GP|DV|DC|STD|ST|HC|HQ|NOR|RF)?\b",
            RegexOptions.IgnoreCase
        );
        if (!match.Success)
        {
            return null;
        }

        var size = match.Groups["size"].Value;
        var type = match.Groups["type"].Value.ToUpperInvariant();
        return size switch
        {
            "20" => type is "HC" or "HQ" ? "20HC" : "20DV",
            "45" => "45HC",
            _ => type is "HC" or "HQ" ? "40HC" : "40DV",
        };
    }

    private static string BuildFocusedSource(
        AiPricingEmailPayload payload,
        string focusedSourceContent
    )
    {
        var issueTerms = payload.PreviousIssues
            .SelectMany(issue => new[] { issue.RawValue, issue.ColumnName })
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Take(20)
            .ToArray();
        var selectedLines = focusedSourceContent
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
            : focusedSourceContent;

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


    private static string SelectNewestPricingSection(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var lines = value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(line => Regex.Replace(
                line.Trim().TrimStart('>', '|', '-', '*').Trim(),
                @"[ \t]+",
                " "
            ))
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();
        var start = Array.FindIndex(
            lines,
            line => Regex.IsMatch(
                line,
                @"\b(?:pls|please)\s+consider\s+(?:the\s+)?rate\b"
                    + @"|\bpublished\s+fak\b"
                    + @"|\b(?:pls|please)\s+(?:check|see|find)\s+(?:the\s+)?(?:below\s+)?(?:the\s+)?(?:updat(?:e|ed)\s+)?rates?\b"
                    + @"|\bupdat(?:e|ed)\s+rates?\s+for\s+(?:your\s+)?ref(?:erence)?\b",
                RegexOptions.IgnoreCase
            )
        );
        if (start < 0)
        {
            return value.Trim();
        }

        var end = lines.Length;
        for (var index = start + 1; index < lines.Length; index++)
        {
            var line = lines[index];
            if (
                line.StartsWith("Un saludo", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Regards", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Best regards", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Worldwide Logistics", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("发件人:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("De:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("From:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Enviado:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Sent:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Subject:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Asunto:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("·¢¼þÈË:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("·¢ËÍÊ±¼ä:", StringComparison.OrdinalIgnoreCase)
                || line.StartsWith("Ö÷Ìâ:", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(line, @"^[_=-]{3,}$")
            )
            {
                end = index;
                break;
            }
        }

        var selected = lines[start..end];
        var result = new List<string>();
        for (var index = 0; index < selected.Length; index++)
        {
            var line = selected[index];
            if (Regex.IsMatch(
                line,
                @"\b(?:pls|please)\s+consider\s+(?:the\s+)?rate\b",
                RegexOptions.IgnoreCase
            ))
            {
                var builder = new StringBuilder(line);
                while (
                    index + 1 < selected.Length
                    && IsRateContinuation(selected[index + 1])
                )
                {
                    AppendLogicalLine(builder, selected[++index]);
                }

                result.Add(builder.ToString());
                continue;
            }

            if (Regex.IsMatch(
                line,
                @"^(?:POL|POD|POE|COMM(?:ODITY)?)\s*:",
                RegexOptions.IgnoreCase
            ))
            {
                var builder = new StringBuilder(line);
                while (
                    index + 1 < selected.Length
                    && IsFieldContinuation(builder.ToString(), selected[index + 1])
                )
                {
                    AppendLogicalLine(builder, selected[++index]);
                }

                result.Add(builder.ToString());
                continue;
            }

            result.Add(line);
        }

        return string.Join('\n', result);
    }

    private static bool IsRateContinuation(string line)
    {
        if (Regex.IsMatch(
            line,
            @"^(?:[A-Z]\)|POL\s*:|POD\s*:|POE\s*:|COMM(?:ODITY)?\s*:|If\s+big\s+lot\b|BUT\b|Subject\s+to\b|If\s+space\b|Below\s+the\s+details\b|Pls\s+note\b)",
            RegexOptions.IgnoreCase
        ))
        {
            return false;
        }

        return line == ","
            || line.StartsWith("valid ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Carrier ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("with ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("per ", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("(", StringComparison.Ordinal);
    }

    private static bool IsFieldContinuation(string current, string line)
    {
        if (Regex.IsMatch(
            line,
            @"^(?:[A-Z]\)|POL\s*:|POD\s*:|POE\s*:|COMM(?:ODITY)?\s*:|Subject\s+to\b|Below\s+the\s+details\b)",
            RegexOptions.IgnoreCase
        ))
        {
            return false;
        }

        var hasOpenParenthesis = current.Count(character => character == '(')
            > current.Count(character => character == ')');
        return hasOpenParenthesis
            || line.StartsWith("/", StringComparison.Ordinal)
            || Regex.IsMatch(line, @"^(?:USD|EUR|CRC|US\$|\$)\s*\d", RegexOptions.IgnoreCase)
            || current.EndsWith("/", StringComparison.Ordinal)
            || current.EndsWith(",", StringComparison.Ordinal);
    }

    private static void AppendLogicalLine(StringBuilder builder, string value)
    {
        var clean = value.Trim().TrimStart(',').Trim();
        if (string.IsNullOrWhiteSpace(clean))
        {
            return;
        }

        if (builder.Length > 0 && builder[^1] is not ' ' and not '/')
        {
            builder.Append(' ');
        }

        builder.Append(clean);
    }

    private static decimal ReadConfidence(JsonElement element)
    {
        var raw = element.ValueKind switch
        {
            JsonValueKind.Number => element.GetRawText(),
            JsonValueKind.String => element.GetString(),
            _ => null,
        };

        return decimal.TryParse(
            raw,
            NumberStyles.Float | NumberStyles.AllowThousands,
            CultureInfo.InvariantCulture,
            out var value
        )
            && value >= -100_000m
            && value <= 100_000m
                ? value
                : 0m;
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

    private sealed record NarrativeCarrierRate(
        string Carrier,
        decimal Amount
    );

    private sealed record NarrativeOrigin(
        string Port,
        decimal? OriginCharge
    );

    private sealed record NarrativeGroup(
        IReadOnlyList<NarrativeOrigin> Origins,
        IReadOnlyList<string> PortsOfDischarge,
        string? Commodity
    );

    private sealed record AiModelResponse(
        bool Success,
        JsonElement Confidence,
        IReadOnlyCollection<AiPricingEmailResultRow>? Rows,
        IReadOnlyCollection<string>? Warnings
    );
}
