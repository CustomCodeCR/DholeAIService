using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Application.Features.Executions.ExecuteChat;
using Dhole.AI.Contracts.Executions.Request;

namespace Dhole.AI.Api.Endpoints;

public static class AiLogisticsNewsEndpoints
{
    private const string PricingWorkspaceScope = "pricing.workspace.access";
    private const string ProfileKey = "pricing-dashboard-analysis";

    public static IEndpointRouteBuilder MapAiLogisticsNewsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/ai/logistics/news")
            .WithTags("AI Logistics News")
            .RequireAuthorization();

        group
            .MapPost("/analyze", AnalyzeAsync)
            .RequireScope(PricingWorkspaceScope);

        return app;
    }

    private static async Task<IResult> AnalyzeAsync(
        LogisticsNewsAnalysisRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        if (string.IsNullOrWhiteSpace(request.Content))
        {
            return EndpointResults.BadRequest(
                "AI.LogisticsNews.ContentRequired",
                "El contenido de la noticia logística es requerido.",
                httpContext
            );
        }

        var content = request.Content.Trim();
        if (content.Length > 6000)
        {
            return EndpointResults.BadRequest(
                "AI.LogisticsNews.ContentTooLong",
                "La noticia logística no puede superar 6000 caracteres.",
                httpContext
            );
        }

        var systemPrompt = """
            Actúa como analista senior de pricing y operaciones de carga internacional.
            Recibirás una noticia logística escrita por una oficina o agente. Extrae únicamente
            lo que esté explícito o inequívocamente indicado en el texto. No inventes puertos,
            rutas, navieras, fechas ni hechos.

            Tu salida se usará para buscar tarifas PRE-APROBADAS y VIGENTES, pero tú NO debes
            decidir ni modificar tarifas. Solo estructura la noticia.

            Devuelve EXCLUSIVAMENTE JSON válido, sin markdown, con esta forma exacta:
            {
              "summary":"resumen operativo breve",
              "carrierTerms":["EMC"],
              "originTerms":["Ningbo"],
              "destinationTerms":["Balboa"],
              "eventType":"space-unavailable",
              "severity":"high",
              "recommendedObservation":"Alerta logística: ...",
              "confidence":0.95
            }

            Reglas:
            - carrierTerms: códigos, abreviaturas o nombres de naviera presentes o inequívocos.
            - originTerms: puertos/lugares de origen afectados.
            - destinationTerms: POE/POD/destinos afectados.
            - eventType solo puede ser: space-unavailable, rollover, congestion, capacity,
              schedule-change, blank-sailing, equipment-shortage, delay, restriction, other.
            - severity solo puede ser: low, medium, high, critical.
            - confidence debe estar entre 0 y 1.
            - recommendedObservation debe ser clara, profesional y conservar el sentido original.
            - Si un dato no está presente, devuelve el arreglo vacío. No lo inventes.
            - No agregues precios ni recomendaciones comerciales que no estén en la noticia.
            """;

        var source = string.Join(
            " | ",
            new[]
            {
                string.IsNullOrWhiteSpace(request.SourceCountry) ? null : $"País/fuente: {request.SourceCountry.Trim()}",
                string.IsNullOrWhiteSpace(request.Title) ? null : $"Título: {request.Title.Trim()}",
                $"Noticia: {content}",
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );

        var result = await dispatcher.DispatchAsync(
            new ExecuteChatCommand(
                ProfileKey,
                [
                    new AiMessageRequest("system", systemPrompt),
                    new AiMessageRequest("user", source),
                ],
                Variables: null,
                httpContext.TraceIdentifier,
                RequestHash: null,
                httpContext.GetCurrentUserId(),
                httpContext.GetCurrentUserName()
            ),
            cancellationToken
        );

        if (result.IsFailure)
        {
            return EndpointResults.FromResult(result, httpContext);
        }

        if (!TryParseAnalysis(result.Value.Content, out var analysis))
        {
            return EndpointResults.BadRequest(
                "AI.LogisticsNews.InvalidAnalysis",
                "La IA no devolvió una estructura válida para la noticia logística.",
                httpContext
            );
        }

        return EndpointResults.Ok(analysis!);
    }

    private static bool TryParseAnalysis(
        string? content,
        out LogisticsNewsAnalysisResponse? analysis
    )
    {
        analysis = null;
        if (string.IsNullOrWhiteSpace(content))
        {
            return false;
        }

        try
        {
            var cleaned = StripMarkdownFence(content.Trim());
            using var document = JsonDocument.Parse(cleaned);
            var root = document.RootElement;

            var summary = ReadString(root, "summary");
            var observation = ReadString(root, "recommendedObservation");
            if (string.IsNullOrWhiteSpace(summary) || string.IsNullOrWhiteSpace(observation))
            {
                return false;
            }

            var eventType = NormalizeEventType(ReadString(root, "eventType"));
            var severity = NormalizeSeverity(ReadString(root, "severity"));
            var confidence = ReadDecimal(root, "confidence");

            analysis = new LogisticsNewsAnalysisResponse(
                summary.Trim(),
                ReadStringArray(root, "carrierTerms"),
                ReadStringArray(root, "originTerms"),
                ReadStringArray(root, "destinationTerms"),
                eventType,
                severity,
                observation.Trim(),
                Math.Clamp(confidence ?? 0.5m, 0m, 1m)
            );
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string StripMarkdownFence(string value)
    {
        if (!value.StartsWith("```", StringComparison.Ordinal))
        {
            return value;
        }

        var firstNewLine = value.IndexOf('\n');
        var lastFence = value.LastIndexOf("```", StringComparison.Ordinal);
        return firstNewLine >= 0 && lastFence > firstNewLine
            ? value[(firstNewLine + 1)..lastFence].Trim()
            : value;
    }

    private static string? ReadString(JsonElement element, string property)
    {
        return element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
    }

    private static string[] ReadStringArray(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        return value
            .EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim())
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .Select(item => item!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToArray();
    }

    private static decimal? ReadDecimal(JsonElement element, string property)
    {
        if (!element.TryGetProperty(property, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetDecimal(out var number))
        {
            return number;
        }

        return value.ValueKind == JsonValueKind.String
            && decimal.TryParse(value.GetString(), out var parsed)
                ? parsed
                : null;
    }

    private static string NormalizeEventType(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is
            "space-unavailable" or
            "rollover" or
            "congestion" or
            "capacity" or
            "schedule-change" or
            "blank-sailing" or
            "equipment-shortage" or
            "delay" or
            "restriction"
                ? normalized
                : "other";
    }

    private static string NormalizeSeverity(string? value)
    {
        var normalized = value?.Trim().ToLowerInvariant();
        return normalized is "low" or "medium" or "high" or "critical"
            ? normalized
            : "medium";
    }
}

public sealed record LogisticsNewsAnalysisRequest(
    string Content,
    string? Title,
    string? SourceCountry
);

public sealed record LogisticsNewsAnalysisResponse(
    string Summary,
    string[] CarrierTerms,
    string[] OriginTerms,
    string[] DestinationTerms,
    string EventType,
    string Severity,
    string RecommendedObservation,
    decimal Confidence
);
