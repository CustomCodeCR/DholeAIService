using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dhole.AI.Application.Abstractions.Services;
using Dhole.AI.Contracts.Executions.Request;
using Dhole.AI.Contracts.Executions.Response;

namespace Dhole.AI.Api.Services;

public sealed record ExecuteAiFileChatInput(
    string ProfileKey,
    string Prompt,
    IReadOnlyCollection<AiMessageRequest> Messages,
    string? CorrelationId,
    string? RequestHash,
    Guid? RequestedBy,
    string? RequestedByName,
    string? AuthorizationHeader,
    string FileName,
    string? ContentType,
    byte[] Content
);

public sealed class AiFileChatService(
    IHttpClientFactory httpClientFactory,
    IAiExecutionOrchestrator orchestrator,
    IConfiguration configuration
)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly string[] FileActionTerms =
    [
        "export",
        "descarg",
        "gener",
        "crea",
        "convert",
        "transform",
        "format",
        "retorn",
        "devuelv",
        "entreg",
        "guard",
    ];

    private static readonly string[] FileOutputTerms =
    [
        "xlsx",
        "excel",
        "csv",
        "archivo",
        "tabla",
    ];

    private static readonly string[] ExplicitFileRequestPhrases =
    [
        "otro xlsx",
        "otro excel",
        "otro csv",
        "archivo descargable",
        "dame un xlsx",
        "dame un excel",
        "dame un csv",
        "quiero un xlsx",
        "quiero un excel",
        "quiero un csv",
        "nueva tabla",
        "tabla nueva",
    ];

    private static readonly string[] ImportantHeaderTerms =
    [
        "actualizado",
        "agente",
        "pol",
        "poe",
        "pod",
        "equipo",
        "contenedor",
        "naviera",
        "carrier",
        "transito",
        "libres",
        "desde",
        "hasta",
        "total costos",
        "total venta",
        "utilidad",
        "profit",
        "comentarios",
        "via",
    ];

    private readonly string _dataExtractionBaseUrl = ResolveBaseUrl(
        configuration["AI:FileChat:DataExtractionBaseUrl"]
            ?? configuration["DataExtraction:InternalBaseUrl"],
        "http://localhost:5205"
    );

    private readonly string _reportsBaseUrl = ResolveBaseUrl(
        configuration["AI:FileChat:ReportsBaseUrl"],
        "http://localhost:5208"
    );

    private readonly int _maximumRows = ReadPositiveInt(
        configuration["AI:FileChat:MaximumRows"],
        2000,
        5000
    );

    private readonly int _maximumPromptCharacters = ReadPositiveInt(
        configuration["AI:FileChat:MaximumPromptCharacters"],
        12000,
        30000
    );

    private readonly int _maximumHistoryMessages = ReadPositiveInt(
        configuration["AI:FileChat:MaximumHistoryMessages"],
        2,
        6
    );

    public async Task<AiFileChatResultDto> ExecuteAsync(
        ExecuteAiFileChatInput input,
        CancellationToken cancellationToken
    )
    {
        var extraction = await ExtractTabularDataAsync(input, cancellationToken);
        var requestsFile = RequestsGeneratedFile(input.Prompt);
        var pricingAnalysis = TryBuildPricingRateAnalysis(extraction);
        var compactDataJson = !requestsFile && pricingAnalysis is not null
            ? pricingAnalysis.ContextJson
            : BuildCompactDataContext(extraction, _maximumPromptCharacters);
        var prompt = requestsFile
            ? BuildFileTransformationPrompt(input.Prompt, extraction, compactDataJson)
            : BuildAnalysisPrompt(input.Prompt, extraction, compactDataJson, pricingAnalysis);

        var messages = BuildMessages(input.Messages, prompt, _maximumHistoryMessages);

        var chatResult = await orchestrator.ExecuteChatAsync(
            new ExecuteAiChatInput(
                input.ProfileKey,
                messages,
                null,
                input.CorrelationId,
                input.RequestHash,
                input.RequestedBy,
                input.RequestedByName
            ),
            cancellationToken
        );

        if (chatResult.IsFailure)
        {
            throw new AiFileChatException(chatResult.Error.Code, chatResult.Error.Message);
        }

        if (
            chatResult.Value.FinishReason.Equals("Length", StringComparison.OrdinalIgnoreCase)
            && chatResult.Value.TokenUsage.OutputTokens <= 2
            && (requestsFile || pricingAnalysis is null)
        )
        {
            throw new AiFileChatException(
                "AI.FileChatContextLimit",
                "El modelo agotó su ventana de contexto antes de responder. "
                    + "Reduzca el historial del chat o use un modelo con mayor contexto."
            );
        }

        var parsed = TryParseAssistantEnvelope(chatResult.Value.Content);
        if (requestsFile && parsed is null)
        {
            throw new AiFileChatException(
                "AI.InvalidFileChatResponse",
                "El modelo no devolvió el contrato JSON requerido para generar el archivo."
            );
        }

        var fallbackAnswer = pricingAnalysis?.Narrative
            ?? BuildGenericAnalysisFallback(extraction);
        var useDeterministicPricingAnswer =
            pricingAnalysis is not null && IsGenericPricingAnalysisRequest(input.Prompt);
        var normalizedAnswer = useDeterministicPricingAnswer
            ? pricingAnalysis!.Narrative
            : !string.IsNullOrWhiteSpace(parsed?.Answer)
                ? parsed!.Answer!.Trim()
                : fallbackAnswer;

        var normalizedChat = chatResult.Value with
        {
            Content = normalizedAnswer,
        };

        AiGeneratedFileDto? generatedFile = null;
        if (parsed?.File is not null)
        {
            if (extraction.IsTruncated)
            {
                normalizedChat = normalizedChat with
                {
                    Content = AppendTruncationNotice(normalizedChat.Content, extraction),
                };
            }
            else if (parsed.File.Rows.ValueKind == JsonValueKind.Array)
            {
                generatedFile = await GenerateFileAsync(
                    parsed.File,
                    parsed.File.Rows.GetRawText(),
                    input.AuthorizationHeader,
                    cancellationToken
                );
            }
            else if (parsed.File.Columns is { Count: > 0 })
            {
                var transformedRows = ApplyTransformation(extraction, parsed.File);
                var rowsJson = JsonSerializer.Serialize(transformedRows, JsonOptions);
                generatedFile = await GenerateFileAsync(
                    parsed.File,
                    rowsJson,
                    input.AuthorizationHeader,
                    cancellationToken
                );
            }
        }
        if (requestsFile && !extraction.IsTruncated && generatedFile is null)
        {
            throw new AiFileChatException(
                "AI.MissingGeneratedFilePlan",
                "El modelo explicó la solicitud, pero no devolvió un plan válido de columnas para generar el archivo."
            );
        }

        return new AiFileChatResultDto(
            normalizedChat,
            extraction.FileName,
            extraction.TotalRows,
            extraction.IsTruncated,
            generatedFile
        );
    }

    private async Task<TabularExtractionResponse> ExtractTabularDataAsync(
        ExecuteAiFileChatInput input,
        CancellationToken cancellationToken
    )
    {
        using var content = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(input.Content);
        fileContent.Headers.ContentType = MediaTypeHeaderValue.TryParse(
            input.ContentType,
            out var parsedContentType
        )
            ? parsedContentType
            : new MediaTypeHeaderValue("application/octet-stream");
        content.Add(fileContent, "file", input.FileName);
        content.Add(new StringContent(_maximumRows.ToString(CultureInfo.InvariantCulture)), "maximumRows");

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_dataExtractionBaseUrl}/api/data-extraction/tabular/extract"
        )
        {
            Content = content,
        };
        ForwardAuthorization(request, input.AuthorizationHeader);

        var client = httpClientFactory.CreateClient("ai-file-processing");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );
        var payload = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new AiFileChatException(
                "AI.FileExtractionFailed",
                ReadErrorMessage(payload, "No fue posible extraer los datos del archivo adjunto.")
            );
        }

        var result = DeserializeWrapped<TabularExtractionResponse>(payload);
        if (result is null)
        {
            throw new AiFileChatException(
                "AI.InvalidFileExtractionResponse",
                "DataExtraction devolvió una respuesta que no pudo interpretarse."
            );
        }

        return result;
    }

    private async Task<AiGeneratedFileDto> GenerateFileAsync(
        AssistantFileEnvelope file,
        string rowsJson,
        string? authorizationHeader,
        CancellationToken cancellationToken
    )
    {
        var format = file.Format?.Trim().ToLowerInvariant();
        if (format is not ("xlsx" or "csv"))
        {
            throw new AiFileChatException(
                "AI.UnsupportedGeneratedFileFormat",
                "La IA solicitó un formato de archivo no compatible."
            );
        }

        var requestBody = JsonSerializer.Serialize(
            new
            {
                format,
                dataJson = "{\"rows\":" + rowsJson + "}",
                fileName = SanitizeFileName(file.FileName),
                sheetName = string.IsNullOrWhiteSpace(file.SheetName)
                    ? "Resultado"
                    : file.SheetName.Trim(),
            },
            JsonOptions
        );

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{_reportsBaseUrl}/api/reports/tabular/generate"
        )
        {
            Content = new StringContent(requestBody, Encoding.UTF8, "application/json"),
        };
        ForwardAuthorization(request, authorizationHeader);

        var client = httpClientFactory.CreateClient("ai-file-processing");
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorPayload = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new AiFileChatException(
                "AI.FileGenerationFailed",
                ReadErrorMessage(errorPayload, "No fue posible generar el archivo solicitado.")
            );
        }

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        var contentType = response.Content.Headers.ContentType?.ToString()
            ?? (format == "xlsx"
                ? "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
                : "text/csv; charset=utf-8");

        var fileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName
            ?? $"{SanitizeFileName(file.FileName)}.{format}";
        fileName = fileName.Trim('"');

        return new AiGeneratedFileDto(
            fileName,
            contentType,
            Convert.ToBase64String(bytes),
            bytes.LongLength
        );
    }

    private static IReadOnlyCollection<Dictionary<string, object?>> ApplyTransformation(
        TabularExtractionResponse extraction,
        AssistantFileEnvelope file
    )
    {
        var columns = file.Columns?.Where(column => !string.IsNullOrWhiteSpace(column.Source)).ToArray()
            ?? [];
        if (columns.Length == 0)
        {
            throw new AiFileChatException(
                "AI.InvalidFileTransformation",
                "La IA no indicó qué columnas debe incluir el archivo generado."
            );
        }

        var availableHeaders = extraction.Sheets
            .SelectMany(sheet => sheet.Headers)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var column in columns)
        {
            if (!availableHeaders.Contains(column.Source!, StringComparer.OrdinalIgnoreCase))
            {
                throw new AiFileChatException(
                    "AI.InvalidFileTransformation",
                    $"La columna de origen '{column.Source}' no existe en el archivo adjunto."
                );
            }
        }

        var filters = file.Filters ?? [];
        if (filters.Count > 0)
        {
            foreach (var filter in filters)
            {
                if (
                    string.IsNullOrWhiteSpace(filter.Source)
                    || !availableHeaders.Contains(filter.Source, StringComparer.OrdinalIgnoreCase)
                )
                {
                    throw new AiFileChatException(
                        "AI.InvalidFileTransformation",
                        $"El filtro usa una columna inexistente: '{filter.Source}'."
                    );
                }
            }
        }

        IEnumerable<TabularRowResponse> sourceRows = extraction.Sheets.SelectMany(sheet => sheet.Rows);
        if (filters.Count > 0)
        {
            sourceRows = sourceRows.Where(row => filters.All(filter => MatchesFilter(row, filter)));
        }

        var maximumRows = file.Take is > 0 ? Math.Min(file.Take.Value, extraction.IncludedRows) : int.MaxValue;
        var result = new List<Dictionary<string, object?>>();

        foreach (var row in sourceRows.Take(maximumRows))
        {
            var output = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in columns)
            {
                var header = string.IsNullOrWhiteSpace(column.Header)
                    ? column.Source!.Trim()
                    : column.Header.Trim();
                var rawValue = GetValue(row.Values, column.Source!);
                output[header] = ConvertOutputValue(rawValue, column.Type, column.DefaultValue);
            }
            result.Add(output);
        }

        return result;
    }

    private static bool MatchesFilter(TabularRowResponse row, AssistantFilterEnvelope filter)
    {
        var rawValue = GetValue(row.Values, filter.Source!);
        var actual = rawValue?.Trim() ?? string.Empty;
        var expected = filter.Value?.Trim() ?? string.Empty;
        var operation = filter.Operator?.Trim().ToLowerInvariant() ?? "equals";

        if (operation is "isempty" or "is_empty")
        {
            return string.IsNullOrWhiteSpace(actual) || actual is "-" or "$ -";
        }
        if (operation is "isnotempty" or "is_not_empty")
        {
            return !string.IsNullOrWhiteSpace(actual) && actual != "-" && actual != "$ -";
        }

        if (
            (operation is "greaterthan" or "greater_than" or "greaterthanorequal" or "greater_than_or_equal"
                or "lessthan" or "less_than" or "lessthanorequal" or "less_than_or_equal")
            && TryParseDecimal(actual, out var actualNumber)
            && TryParseDecimal(expected, out var expectedNumber)
        )
        {
            return operation switch
            {
                "greaterthan" or "greater_than" => actualNumber > expectedNumber,
                "greaterthanorequal" or "greater_than_or_equal" => actualNumber >= expectedNumber,
                "lessthan" or "less_than" => actualNumber < expectedNumber,
                _ => actualNumber <= expectedNumber,
            };
        }

        return operation switch
        {
            "notequals" or "not_equals" => !actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
            "contains" => actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "notcontains" or "not_contains" => !actual.Contains(expected, StringComparison.OrdinalIgnoreCase),
            "startswith" or "starts_with" => actual.StartsWith(expected, StringComparison.OrdinalIgnoreCase),
            "endswith" or "ends_with" => actual.EndsWith(expected, StringComparison.OrdinalIgnoreCase),
            _ => actual.Equals(expected, StringComparison.OrdinalIgnoreCase),
        };
    }

    private static object? ConvertOutputValue(string? rawValue, string? requestedType, string? defaultValue)
    {
        var value = string.IsNullOrWhiteSpace(rawValue) ? defaultValue : rawValue;
        if (string.IsNullOrWhiteSpace(value) || value.Trim() == "-" || value.Trim() == "$ -")
        {
            return null;
        }

        var type = requestedType?.Trim().ToLowerInvariant() ?? "string";
        return type switch
        {
            "number" or "decimal" or "currency" or "money" =>
                TryParseDecimal(value, out var number) ? number : value,
            "integer" or "int" =>
                TryParseDecimal(value, out var integer) ? decimal.ToInt64(decimal.Truncate(integer)) : value,
            "date" => TryParseDate(value, out var date) ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : value,
            "boolean" or "bool" => TryParseBoolean(value, out var boolean) ? boolean : value,
            _ => value.Trim(),
        };
    }

    private static string BuildAnalysisPrompt(
        string userPrompt,
        TabularExtractionResponse extraction,
        string compactDataJson,
        PricingRateAnalysis? pricingAnalysis
    )
    {
        var pricingInstructions = pricingAnalysis is null
            ? string.Empty
            : $$"""

            El archivo fue identificado como una matriz de tarifas logísticas.

            Reglas obligatorias para tarifas:
            - Cada fila representa una alternativa de tarifa por POL/ruta; NO representa un contenedor contratado.
            - Nunca sume todas las tarifas ni presente esa suma como costo, venta o valor total.
            - Compare por tipo de equipo usando mínimo, máximo, diferencia y puertos asociados.
            - El promedio solo puede mencionarse si el usuario lo solicita y debe llamarse "promedio de alternativas".
            - Destaque naviera, moneda, vigencia, puerto de salida, modalidad y condiciones comerciales disponibles.
            - Para una solicitud genérica de análisis, explique cuáles son las opciones más económicas y más costosas.
            """;

        return $$"""
            Está analizando un archivo tabular adjunto dentro del asistente de Dhole.

            Responda ÚNICAMENTE con JSON válido, sin bloques Markdown y con exactamente estas propiedades:
            {
              "answer": "texto del análisis en español",
              "file": null
            }

            Reglas del contrato:
            - answer DEBE ser un string. No use un objeto, arreglo ni propiedades anidadas dentro de answer.
            - file DEBE ser null porque el usuario no solicitó una exportación.
            - No agregue propiedades distintas de answer y file.

            Reglas de análisis:
            - Analice el archivo completo utilizando las estadísticas y muestras preparadas por el backend.
            - Destaque rangos, diferencias, valores atípicos, datos faltantes y riesgos relevantes para la solicitud.
            - No invente valores ni afirme haber revisado información que no aparezca en el contexto.
            - Sea concreto, pero incluya cifras importantes.
            {{pricingInstructions}}

            Solicitud del usuario:
            {{userPrompt}}

            Archivo: {{extraction.FileName}}
            Filas totales: {{extraction.TotalRows}}
            Filas incluidas: {{extraction.IncludedRows}}
            Datos truncados: {{extraction.IsTruncated}}

            Perfil compacto del archivo:
            {{compactDataJson}}
            """;
    }

    private static string BuildFileTransformationPrompt(
        string userPrompt,
        TabularExtractionResponse extraction,
        string compactDataJson
    )
    {
        return $$"""
            Está preparando una transformación de un archivo tabular dentro del asistente de Dhole.

            Responda ÚNICAMENTE con JSON válido, sin bloques Markdown, usando esta estructura:
            {
              "answer": "explicación breve de la transformación",
              "file": {
                "format": "xlsx",
                "fileName": "resultado",
                "sheetName": "Resultado",
                "columns": [
                  {
                    "header": "Nombre de columna de salida",
                    "source": "ENCABEZADO EXACTO DEL ARCHIVO",
                    "type": "string",
                    "defaultValue": null
                  }
                ],
                "filters": [],
                "take": null
              }
            }

            Operadores permitidos para filters:
            equals, notEquals, contains, notContains, startsWith, endsWith,
            isEmpty, isNotEmpty, greaterThan, greaterThanOrEqual, lessThan, lessThanOrEqual.

            Tipos permitidos para columns:
            string, number, currency, integer, date, boolean.

            Reglas:
            - No devuelva todas las filas en el JSON.
            - Defina solamente el plan de columnas y filtros; el backend lo aplicará a todas las filas.
            - source debe coincidir exactamente con uno de los encabezados disponibles.
            - Use format xlsx o csv según lo solicitado; si no se especifica, use xlsx.
            - Incluya solo las columnas necesarias para cumplir la solicitud.
            - Use filters únicamente cuando el usuario realmente solicite filtrar registros.
            - Si Datos truncados es true, use file: null y explíquelo en answer.
            - No invente datos ni columnas calculadas que no existan en el origen.

            Solicitud del usuario:
            {{userPrompt}}

            Archivo: {{extraction.FileName}}
            Filas totales: {{extraction.TotalRows}}
            Filas incluidas: {{extraction.IncludedRows}}
            Datos truncados: {{extraction.IsTruncated}}

            Perfil compacto del archivo:
            {{compactDataJson}}
            """;
    }

    private static IReadOnlyCollection<AiExecutionMessageInput> BuildMessages(
        IReadOnlyCollection<AiMessageRequest> history,
        string prompt,
        int maximumHistoryMessages
    )
    {
        return history
            .Where(message => !string.IsNullOrWhiteSpace(message.Content))
            .TakeLast(maximumHistoryMessages)
            .Select(message => new AiExecutionMessageInput(message.Role, Truncate(message.Content, 1000)))
            .Append(new AiExecutionMessageInput("user", prompt))
            .ToArray();
    }

    private static string BuildCompactDataContext(
        TabularExtractionResponse extraction,
        int maximumCharacters
    )
    {
        var full = BuildContextObject(extraction, includeAllProfiles: true, includeExamples: true, sampleCount: 5);
        var fullJson = JsonSerializer.Serialize(full, JsonOptions);
        if (fullJson.Length <= maximumCharacters)
        {
            return fullJson;
        }

        var reduced = BuildContextObject(extraction, includeAllProfiles: true, includeExamples: false, sampleCount: 3);
        var reducedJson = JsonSerializer.Serialize(reduced, JsonOptions);
        if (reducedJson.Length <= maximumCharacters)
        {
            return reducedJson;
        }

        var important = BuildContextObject(extraction, includeAllProfiles: false, includeExamples: false, sampleCount: 3);
        var importantJson = JsonSerializer.Serialize(important, JsonOptions);
        if (importantJson.Length <= maximumCharacters)
        {
            return importantJson;
        }

        var minimum = new
        {
            extraction.FileName,
            extraction.FileType,
            extraction.TotalRows,
            extraction.IncludedRows,
            extraction.IsTruncated,
            Sheets = extraction.Sheets.Select(sheet => new
            {
                sheet.Name,
                RowCount = sheet.Rows.Count,
                Headers = sheet.Headers,
                Samples = SelectRepresentativeRows(sheet.Rows, 2)
                    .Select(row => new { row.RowNumber, row.Values }),
            }),
        };

        return JsonSerializer.Serialize(minimum, JsonOptions);
    }

    private static object BuildContextObject(
        TabularExtractionResponse extraction,
        bool includeAllProfiles,
        bool includeExamples,
        int sampleCount
    )
    {
        return new
        {
            extraction.FileName,
            extraction.FileType,
            extraction.TotalRows,
            extraction.IncludedRows,
            extraction.IsTruncated,
            Sheets = extraction.Sheets.Select(sheet =>
            {
                var selectedHeaders = SelectImportantHeaders(sheet.Headers, includeAllProfiles ? 60 : 18);
                return new
                {
                    sheet.Name,
                    RowCount = sheet.Rows.Count,
                    Headers = sheet.Headers,
                    ColumnProfiles = selectedHeaders.Select(header =>
                        BuildColumnProfile(sheet.Rows, header, includeExamples)
                    ),
                    Samples = SelectRepresentativeRows(sheet.Rows, sampleCount)
                        .Select(row => new
                        {
                            row.RowNumber,
                            Values = selectedHeaders.ToDictionary(
                                header => header,
                                header => GetValue(row.Values, header),
                                StringComparer.OrdinalIgnoreCase
                            ),
                        }),
                };
            }),
        };
    }

    private static ColumnProfile BuildColumnProfile(
        IReadOnlyCollection<TabularRowResponse> rows,
        string header,
        bool includeExamples
    )
    {
        var values = rows
            .Select(row => GetValue(row.Values, header))
            .Where(value => !string.IsNullOrWhiteSpace(value) && value!.Trim() is not ("-" or "$ -"))
            .Select(value => value!.Trim())
            .ToArray();

        var distinctValues = values.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var numericValues = values
            .Select(value => TryParseDecimal(value, out var number) ? (decimal?)number : null)
            .Where(number => number.HasValue)
            .Select(number => number!.Value)
            .ToArray();
        var dateValues = values
            .Select(value => TryParseDate(value, out var date) ? (DateTime?)date : null)
            .Where(date => date.HasValue)
            .Select(date => date!.Value)
            .ToArray();

        string detectedType;
        object? summary = null;
        if (numericValues.Length >= Math.Max(2, (int)Math.Ceiling(values.Length * 0.6m)))
        {
            detectedType = "number";
            summary = new
            {
                Minimum = numericValues.Min(),
                Maximum = numericValues.Max(),
                Average = Math.Round(numericValues.Average(), 2),
                Sum = numericValues.Sum(),
            };
        }
        else if (dateValues.Length >= Math.Max(2, (int)Math.Ceiling(values.Length * 0.6m)))
        {
            detectedType = "date";
            summary = new
            {
                Minimum = dateValues.Min().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                Maximum = dateValues.Max().ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            };
        }
        else
        {
            detectedType = "text";
        }

        return new ColumnProfile(
            header,
            detectedType,
            values.Length,
            rows.Count - values.Length,
            distinctValues.Length,
            includeExamples
                ? distinctValues.Take(3).Select(value => Truncate(value, 100)).ToArray()
                : [],
            summary
        );
    }

    private static IReadOnlyCollection<string> SelectImportantHeaders(
        IReadOnlyCollection<string> headers,
        int maximum
    )
    {
        var important = headers
            .Where(header => ImportantHeaderTerms.Any(term =>
                header.Contains(term, StringComparison.OrdinalIgnoreCase)
            ))
            .ToList();

        foreach (var header in headers)
        {
            if (important.Count >= maximum)
            {
                break;
            }
            if (!important.Contains(header, StringComparer.OrdinalIgnoreCase))
            {
                important.Add(header);
            }
        }

        return important.Take(maximum).ToArray();
    }

    private static IReadOnlyCollection<TabularRowResponse> SelectRepresentativeRows(
        IReadOnlyCollection<TabularRowResponse> rows,
        int maximum
    )
    {
        if (rows.Count <= maximum)
        {
            return rows;
        }

        var ordered = rows.ToArray();
        var indexes = new[] { 0, ordered.Length / 4, ordered.Length / 2, (ordered.Length * 3) / 4, ordered.Length - 1 };
        return indexes
            .Distinct()
            .Take(maximum)
            .Select(index => ordered[index])
            .ToArray();
    }

    private static PricingRateAnalysis? TryBuildPricingRateAnalysis(
        TabularExtractionResponse extraction
    )
    {
        foreach (var sheet in extraction.Sheets)
        {
            var polHeader = sheet.Headers.FirstOrDefault(header =>
                NormalizeHeaderKey(header) is "pol" or "portofloading" or "originport"
            );
            if (polHeader is null)
            {
                continue;
            }

            var amountHeaders = sheet.Headers
                .Where(IsContainerRateHeader)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (amountHeaders.Length == 0)
            {
                continue;
            }

            var equipment = new List<PricingEquipmentAnalysis>();
            foreach (var amountHeader in amountHeaders)
            {
                var values = sheet.Rows
                    .Select(row => new
                    {
                        Pol = GetValue(row.Values, polHeader)?.Trim(),
                        RawAmount = GetValue(row.Values, amountHeader),
                    })
                    .Where(item =>
                        !string.IsNullOrWhiteSpace(item.Pol)
                        && !string.IsNullOrWhiteSpace(item.RawAmount)
                        && TryParseDecimal(item.RawAmount!, out _)
                    )
                    .Select(item => new PricingRouteAmount(
                        item.Pol!,
                        ParseRequiredDecimal(item.RawAmount!)
                    ))
                    .ToArray();

                if (values.Length == 0)
                {
                    continue;
                }

                var minimum = values.Min(item => item.Amount);
                var maximum = values.Max(item => item.Amount);
                equipment.Add(new PricingEquipmentAnalysis(
                    amountHeader,
                    values.Length,
                    minimum,
                    maximum,
                    Math.Round(values.Average(item => item.Amount), 2),
                    maximum - minimum,
                    values
                        .Where(item => item.Amount == minimum)
                        .Select(item => item.Pol)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray(),
                    values
                        .Where(item => item.Amount == maximum)
                        .Select(item => item.Pol)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToArray()
                ));
            }

            if (equipment.Count == 0)
            {
                continue;
            }

            var carriers = GetDistinctColumnValues(sheet, "carrier", "naviera");
            var currencies = GetDistinctColumnValues(sheet, "currency", "moneda");
            var portsOfExit = GetDistinctColumnValues(sheet, "poe", "portofexit");
            var routeModes = GetDistinctColumnValues(sheet, "routemode", "modalidad");
            var validFrom = GetDateRangeBoundary(sheet, true, "validfrom", "desde");
            var validTo = GetDateRangeBoundary(sheet, false, "validto", "hasta");
            var remarks = GetDistinctColumnValues(sheet, "remarks", "comentarios", "observaciones")
                .Where(value => !value.StartsWith("Tarifa ", StringComparison.OrdinalIgnoreCase)
                    || value.Contains("Condiciones", StringComparison.OrdinalIgnoreCase))
                .ToArray();

            var totalAlternatives = sheet.Rows.Count(row =>
                !string.IsNullOrWhiteSpace(GetValue(row.Values, polHeader))
                && amountHeaders.Any(header =>
                    GetValue(row.Values, header) is { } raw && TryParseDecimal(raw, out _)
                )
            );
            var currency = currencies.FirstOrDefault() ?? "USD";
            var narrative = BuildPricingNarrative(
                extraction.FileName,
                totalAlternatives,
                carriers,
                currency,
                portsOfExit,
                routeModes,
                validFrom,
                validTo,
                equipment,
                remarks
            );

            var contextJson = JsonSerializer.Serialize(
                new
                {
                    Type = "pricing-rate-matrix",
                    Interpretation = "Cada fila es una alternativa de tarifa por POL y no una cantidad de contenedores. No se deben sumar todas las filas.",
                    TotalAlternatives = totalAlternatives,
                    Carriers = carriers,
                    Currencies = currencies,
                    PortsOfExit = portsOfExit,
                    RouteModes = routeModes,
                    ValidFrom = validFrom,
                    ValidTo = validTo,
                    Equipment = equipment.Select(item => new
                    {
                        item.Header,
                        item.Count,
                        item.Minimum,
                        item.Maximum,
                        item.Spread,
                        MinimumPorts = item.MinimumPorts,
                        MaximumPorts = item.MaximumPorts,
                    }),
                    CommercialConditions = remarks,
                },
                JsonOptions
            );

            return new PricingRateAnalysis(narrative, contextJson);
        }

        return null;
    }

    private static string BuildPricingNarrative(
        string fileName,
        int totalAlternatives,
        IReadOnlyCollection<string> carriers,
        string currency,
        IReadOnlyCollection<string> portsOfExit,
        IReadOnlyCollection<string> routeModes,
        string? validFrom,
        string? validTo,
        IReadOnlyCollection<PricingEquipmentAnalysis> equipment,
        IReadOnlyCollection<string> remarks
    )
    {
        var builder = new StringBuilder();
        builder.Append("El archivo ")
            .Append(fileName)
            .Append(" contiene ")
            .Append(totalAlternatives)
            .Append(" alternativas de tarifa por puerto de origen (POL). ")
            .Append("No son ")
            .Append(totalAlternatives)
            .Append(" contenedores ni corresponde sumar todas las tarifas entre sí.");

        var commercialDetails = new List<string>();
        if (carriers.Count > 0)
        {
            commercialDetails.Add($"naviera {JoinValues(carriers)}");
        }
        commercialDetails.Add($"moneda {currency}");
        if (portsOfExit.Count > 0)
        {
            commercialDetails.Add($"salida/vía {JoinValues(portsOfExit)}");
        }
        if (routeModes.Count > 0)
        {
            commercialDetails.Add($"modalidad {JoinValues(routeModes)}");
        }
        if (validFrom is not null || validTo is not null)
        {
            commercialDetails.Add($"vigencia {FormatDateRange(validFrom, validTo)}");
        }

        builder.Append("\n\nDatos generales: ")
            .Append(string.Join(", ", commercialDetails))
            .Append('.');

        foreach (var item in equipment)
        {
            builder.Append("\n\n")
                .Append(item.Header)
                .Append(": la opción mínima es ")
                .Append(FormatMoney(item.Minimum, currency))
                .Append(" desde ")
                .Append(FormatPortList(item.MinimumPorts))
                .Append("; la máxima es ")
                .Append(FormatMoney(item.Maximum, currency))
                .Append(" desde ")
                .Append(FormatPortList(item.MaximumPorts))
                .Append(". La diferencia entre ambas es ")
                .Append(FormatMoney(item.Spread, currency))
                .Append('.');
        }

        if (remarks.Count > 0)
        {
            builder.Append("\n\nCondiciones importantes: ")
                .Append(string.Join(" ", remarks.Select(value => value.Trim())))
                .Append(' ');
        }

        builder.Append("\n\nPara tomar una decisión, compare el POL y el tipo de equipo requerido; el promedio de todas las rutas no representa una cotización real.");
        return builder.ToString().Trim();
    }

    private static string BuildGenericAnalysisFallback(TabularExtractionResponse extraction)
    {
        var sheetSummaries = extraction.Sheets
            .Select(sheet => $"{sheet.Name ?? "Hoja"}: {sheet.Rows.Count} filas y {sheet.Headers.Count} columnas")
            .ToArray();
        return $"El archivo {extraction.FileName} contiene {extraction.TotalRows} filas. "
            + string.Join("; ", sheetSummaries)
            + ". El modelo devolvió un formato de respuesta inválido, por lo que se muestra este resumen estructural en lugar del JSON crudo.";
    }

    private static bool IsGenericPricingAnalysisRequest(string prompt)
    {
        var normalized = NormalizeHeaderKey(prompt);
        return prompt.Length <= 140
            && (normalized.Contains("analiz", StringComparison.Ordinal)
                || normalized.Contains("resum", StringComparison.Ordinal)
                || normalized.Contains("revis", StringComparison.Ordinal))
            && (normalized.Contains("tarif", StringComparison.Ordinal)
                || normalized.Contains("archivo", StringComparison.Ordinal)
                || normalized.Contains("xlsx", StringComparison.Ordinal)
                || normalized.Contains("excel", StringComparison.Ordinal));
    }

    private static bool IsContainerRateHeader(string header)
    {
        var normalized = NormalizeHeaderKey(header);
        return normalized is "20dv" or "20std" or "20gp" or "20dc"
            or "40dv" or "40std" or "40gp" or "40dc" or "40hc" or "40hq"
            or "40dvhc" or "40dvhq" or "40stdhc";
    }

    private static string[] GetDistinctColumnValues(
        TabularSheetResponse sheet,
        params string[] normalizedHeaderCandidates
    )
    {
        var header = sheet.Headers.FirstOrDefault(value =>
            normalizedHeaderCandidates.Contains(NormalizeHeaderKey(value), StringComparer.Ordinal)
        );
        if (header is null)
        {
            return [];
        }

        return sheet.Rows
            .Select(row => GetValue(row.Values, header)?.Trim())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string? GetDateRangeBoundary(
        TabularSheetResponse sheet,
        bool minimum,
        params string[] normalizedHeaderCandidates
    )
    {
        var header = sheet.Headers.FirstOrDefault(value =>
            normalizedHeaderCandidates.Contains(NormalizeHeaderKey(value), StringComparer.Ordinal)
        );
        if (header is null)
        {
            return null;
        }

        var dates = sheet.Rows
            .Select(row => GetValue(row.Values, header))
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => TryParseDate(value!, out var date) ? (DateTime?)date : null)
            .Where(value => value.HasValue)
            .Select(value => value!.Value)
            .ToArray();
        if (dates.Length == 0)
        {
            return null;
        }

        return (minimum ? dates.Min() : dates.Max())
            .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static decimal ParseRequiredDecimal(string value)
    {
        return TryParseDecimal(value, out var parsed) ? parsed : 0m;
    }

    private static string NormalizeHeaderKey(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }
        return builder.ToString();
    }

    private static string FormatMoney(decimal value, string currency)
    {
        return $"{currency} {value.ToString("N0", CultureInfo.GetCultureInfo("en-US"))}";
    }

    private static string FormatDateRange(string? validFrom, string? validTo)
    {
        var formattedFrom = FormatDateValue(validFrom);
        var formattedTo = FormatDateValue(validTo);
        if (formattedFrom is not null && formattedTo is not null)
        {
            return $"del {formattedFrom} al {formattedTo}";
        }
        return formattedFrom is not null
            ? $"desde {formattedFrom}"
            : $"hasta {formattedTo}";
    }

    private static string? FormatDateValue(string? value)
    {
        return value is not null && TryParseDate(value, out var date)
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : value;
    }

    private static string FormatPortList(IReadOnlyCollection<string> ports)
    {
        if (ports.Count == 0)
        {
            return "un POL no identificado";
        }

        const int maximumVisible = 10;
        var visible = ports.Take(maximumVisible).ToArray();
        var text = JoinValues(visible);
        return ports.Count > maximumVisible
            ? $"{text} y {ports.Count - maximumVisible} opciones más"
            : text;
    }

    private static string JoinValues(IEnumerable<string> values)
    {
        var items = values.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray();
        return items.Length switch
        {
            0 => string.Empty,
            1 => items[0],
            2 => $"{items[0]} y {items[1]}",
            _ => string.Join(", ", items[..^1]) + " y " + items[^1],
        };
    }

    private static bool RequestsGeneratedFile(string prompt)
    {
        if (ExplicitFileRequestPhrases.Any(phrase =>
            prompt.Contains(phrase, StringComparison.OrdinalIgnoreCase)
        ))
        {
            return true;
        }

        var hasAction = FileActionTerms.Any(term =>
            prompt.Contains(term, StringComparison.OrdinalIgnoreCase)
        );
        var hasOutput = FileOutputTerms.Any(term =>
            prompt.Contains(term, StringComparison.OrdinalIgnoreCase)
        );
        return hasAction && hasOutput;
    }

    private static string AppendTruncationNotice(
        string content,
        TabularExtractionResponse extraction
    )
    {
        var notice =
            $"No se generó el archivo porque el origen contiene {extraction.TotalRows} filas "
            + $"y el límite configurado permitió analizar {extraction.IncludedRows}. "
            + "Aumente AI:FileChat:MaximumRows o reduzca el archivo para exportar un resultado completo.";

        return string.IsNullOrWhiteSpace(content)
            ? notice
            : $"{content.Trim()}\n\n{notice}";
    }

    private static AssistantEnvelope? TryParseAssistantEnvelope(string content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return null;
        }

        var normalized = content.Trim();
        if (normalized.StartsWith("```", StringComparison.Ordinal))
        {
            var firstLineEnd = normalized.IndexOf('\n');
            if (firstLineEnd >= 0)
            {
                normalized = normalized[(firstLineEnd + 1)..];
            }
            if (normalized.EndsWith("```", StringComparison.Ordinal))
            {
                normalized = normalized[..^3];
            }
        }

        var firstBrace = normalized.IndexOf('{');
        var lastBrace = normalized.LastIndexOf('}');
        if (firstBrace < 0 || lastBrace <= firstBrace)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(normalized[firstBrace..(lastBrace + 1)]);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            string? answer = null;
            if (
                TryGetPropertyIgnoreCase(root, "answer", out var answerElement)
                && answerElement.ValueKind == JsonValueKind.String
            )
            {
                answer = answerElement.GetString();
            }

            AssistantFileEnvelope? file = null;
            if (TryGetPropertyIgnoreCase(root, "file", out var fileElement))
            {
                if (fileElement.ValueKind == JsonValueKind.Object)
                {
                    file = JsonSerializer.Deserialize<AssistantFileEnvelope>(
                        fileElement.GetRawText(),
                        JsonOptions
                    );
                }
                else if (fileElement.ValueKind != JsonValueKind.Null)
                {
                    return null;
                }
            }

            return new AssistantEnvelope(answer, file);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static T? DeserializeWrapped<T>(string payload)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var current = document.RootElement;

            for (var index = 0; index < 4 && current.ValueKind == JsonValueKind.Object; index++)
            {
                if (TryGetPropertyIgnoreCase(current, "data", out var data))
                {
                    current = data;
                    continue;
                }
                if (TryGetPropertyIgnoreCase(current, "value", out var value))
                {
                    current = value;
                    continue;
                }
                if (TryGetPropertyIgnoreCase(current, "result", out var result))
                {
                    current = result;
                    continue;
                }
                break;
            }

            return JsonSerializer.Deserialize<T>(current.GetRawText(), JsonOptions);
        }
        catch (JsonException)
        {
            return default;
        }
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value
    )
    {
        foreach (var property in element.EnumerateObject())
        {
            if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    private static string ReadErrorMessage(string payload, string fallback)
    {
        try
        {
            using var document = JsonDocument.Parse(payload);
            var root = document.RootElement;
            foreach (var propertyName in new[] { "detail", "message", "title" })
            {
                if (
                    TryGetPropertyIgnoreCase(root, propertyName, out var value)
                    && value.ValueKind == JsonValueKind.String
                    && !string.IsNullOrWhiteSpace(value.GetString())
                )
                {
                    return value.GetString()!;
                }
            }
        }
        catch (JsonException)
        {
            // La respuesta puede ser texto plano; se usa el mensaje de respaldo.
        }

        return fallback;
    }

    private static string? GetValue(
        IReadOnlyDictionary<string, string?> values,
        string header
    )
    {
        foreach (var pair in values)
        {
            if (pair.Key.Equals(header, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    private static bool TryParseDecimal(string value, out decimal result)
    {
        var normalized = value.Trim();
        if (normalized is "-" or "$ -")
        {
            result = 0;
            return false;
        }

        var isNegative = normalized.StartsWith('(') && normalized.EndsWith(')');
        normalized = normalized
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Replace("USD", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("CRC", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("₡", string.Empty, StringComparison.Ordinal)
            .Replace("%", string.Empty, StringComparison.Ordinal)
            .Replace("(", string.Empty, StringComparison.Ordinal)
            .Replace(")", string.Empty, StringComparison.Ordinal)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace(",", string.Empty, StringComparison.Ordinal);

        if (
            decimal.TryParse(
                normalized,
                NumberStyles.Number | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out result
            )
        )
        {
            if (isNegative)
            {
                result = -result;
            }
            return true;
        }

        return decimal.TryParse(value, NumberStyles.Any, CultureInfo.CurrentCulture, out result);
    }

    private static bool TryParseDate(string value, out DateTime result)
    {
        foreach (var culture in new[]
        {
            CultureInfo.GetCultureInfo("en-US"),
            CultureInfo.GetCultureInfo("es-CR"),
            CultureInfo.InvariantCulture,
        })
        {
            if (DateTime.TryParse(value, culture, DateTimeStyles.AllowWhiteSpaces, out result))
            {
                return true;
            }
        }

        result = default;
        return false;
    }

    private static bool TryParseBoolean(string value, out bool result)
    {
        if (bool.TryParse(value, out result))
        {
            return true;
        }

        switch (value.Trim().ToLowerInvariant())
        {
            case "sí":
            case "si":
            case "yes":
            case "1":
                result = true;
                return true;
            case "no":
            case "0":
                result = false;
                return true;
            default:
                result = false;
                return false;
        }
    }

    private static void ForwardAuthorization(
        HttpRequestMessage request,
        string? authorizationHeader
    )
    {
        if (!string.IsNullOrWhiteSpace(authorizationHeader))
        {
            request.Headers.TryAddWithoutValidation("Authorization", authorizationHeader);
        }
    }

    private static string ResolveBaseUrl(string? value, string fallback)
    {
        var baseUrl = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return baseUrl.TrimEnd('/');
    }

    private static int ReadPositiveInt(string? value, int fallback, int maximum)
    {
        return int.TryParse(value, out var parsed) && parsed > 0
            ? Math.Min(parsed, maximum)
            : fallback;
    }

    private static string SanitizeFileName(string? value)
    {
        var name = string.IsNullOrWhiteSpace(value) ? "resultado-ia" : value.Trim();
        name = Path.GetFileNameWithoutExtension(name);
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(
            name.Select(character => invalid.Contains(character) ? '-' : character).ToArray()
        );
        return string.IsNullOrWhiteSpace(sanitized) ? "resultado-ia" : sanitized;
    }

    private static string Truncate(string value, int maximumCharacters)
    {
        if (value.Length <= maximumCharacters)
        {
            return value;
        }

        return value[..maximumCharacters] + "…";
    }

    private sealed record TabularExtractionResponse(
        string FileName,
        string FileType,
        IReadOnlyCollection<TabularSheetResponse> Sheets,
        int TotalRows,
        int IncludedRows,
        bool IsTruncated
    );

    private sealed record TabularSheetResponse(
        string? Name,
        IReadOnlyCollection<string> Headers,
        IReadOnlyCollection<TabularRowResponse> Rows
    );

    private sealed record TabularRowResponse(
        int RowNumber,
        IReadOnlyDictionary<string, string?> Values
    );

    private sealed record ColumnProfile(
        string Header,
        string DetectedType,
        int NonEmptyCount,
        int EmptyCount,
        int DistinctCount,
        IReadOnlyCollection<string> Examples,
        object? Summary
    );

    private sealed record PricingRateAnalysis(
        string Narrative,
        string ContextJson
    );

    private sealed record PricingRouteAmount(
        string Pol,
        decimal Amount
    );

    private sealed record PricingEquipmentAnalysis(
        string Header,
        int Count,
        decimal Minimum,
        decimal Maximum,
        decimal Average,
        decimal Spread,
        IReadOnlyCollection<string> MinimumPorts,
        IReadOnlyCollection<string> MaximumPorts
    );

    private sealed record AssistantEnvelope(
        string? Answer,
        AssistantFileEnvelope? File
    );

    private sealed record AssistantFileEnvelope(
        string? Format,
        string? FileName,
        string? SheetName,
        JsonElement Rows,
        IReadOnlyCollection<AssistantColumnEnvelope>? Columns,
        IReadOnlyCollection<AssistantFilterEnvelope>? Filters,
        int? Take
    );

    private sealed record AssistantColumnEnvelope(
        string? Header,
        string? Source,
        string? Type,
        string? DefaultValue
    );

    private sealed record AssistantFilterEnvelope(
        string? Source,
        string? Operator,
        string? Value,
        string? Type
    );
}

public sealed class AiFileChatException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
