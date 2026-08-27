using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Application.Features.Executions.ExecuteChat;
using Dhole.AI.Contracts.Executions.Request;

namespace Dhole.AI.Api.Endpoints;

public static class AiLogisticsEndpoints
{
    private const string PricingWorkspaceScope = "pricing.workspace.access";
    private const string ProfileKey = "assistant";

    public static IEndpointRouteBuilder MapAiLogisticsEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app
            .MapGroup("/api/ai/logistics")
            .WithTags("AI Logistics")
            .RequireAuthorization();

        group
            .MapPost("/nearest-ports", RecommendNearestPortsAsync)
            .RequireScope(PricingWorkspaceScope);

        return app;
    }

    private static async Task<IResult> RecommendNearestPortsAsync(
        NearestPortsRequest request,
        ICommandDispatcher dispatcher,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var pickupAddress = request.PickupAddress?.Trim();
        if (string.IsNullOrWhiteSpace(pickupAddress) && (!request.Latitude.HasValue || !request.Longitude.HasValue))
        {
            return Results.BadRequest(
                new
                {
                    code = "AI.Logistics.PickupLocationRequired",
                    message = "Indique una dirección o coordenadas para recomendar puertos cercanos.",
                }
            );
        }

        var ports = request.Ports
            .Where(port => port.Id != Guid.Empty && !string.IsNullOrWhiteSpace(port.Name))
            .DistinctBy(port => port.Id)
            .Take(120)
            .ToArray();

        if (ports.Length == 0)
        {
            return Results.BadRequest(
                new
                {
                    code = "AI.Logistics.PortCandidatesRequired",
                    message = "No existen puertos de origen configurados para analizar.",
                }
            );
        }

        var candidatesJson = JsonSerializer.Serialize(
            ports.Select(port => new
            {
                id = port.Id,
                name = port.Name.Trim(),
                code = port.Code?.Trim(),
                country = port.Country?.Trim(),
                latitude = port.Latitude,
                longitude = port.Longitude,
            })
        );

        var systemPrompt = """
            Eres un especialista en logística internacional. Debes recomendar únicamente puertos incluidos en la lista de candidatos recibida.
            Determina cuáles son razonablemente más cercanos y útiles para una recolección EXW usando la dirección y, cuando existan, las coordenadas.
            No inventes puertos, IDs ni datos fuera de la lista. Devuelve máximo 3 opciones ordenadas de mejor a peor.
            Responde EXCLUSIVAMENTE JSON válido, sin markdown, con este formato:
            {"recommendations":[{"portId":"GUID","reason":"explicación breve"}]}
            Si no puedes determinar una opción con suficiente confianza, devuelve {"recommendations":[]}.
            """;

        var locationText = request.Latitude.HasValue && request.Longitude.HasValue
            ? $"{pickupAddress ?? "Dirección no indicada"} | coordenadas {request.Latitude.Value}, {request.Longitude.Value}"
            : pickupAddress!;

        var messages = new[]
        {
            new AiMessageRequest("system", systemPrompt),
            new AiMessageRequest(
                "user",
                $"Lugar de recolección: {locationText}\nPuertos candidatos configurados en Dhole: {candidatesJson}"
            ),
        };

        var result = await dispatcher.DispatchAsync(
            new ExecuteChatCommand(
                ProfileKey,
                messages,
                Variables: null,
                httpContext.TraceIdentifier,
                RequestHash: null,
                httpContext.GetCurrentUserId(),
                httpContext.GetCurrentUserName()
            ),
            cancellationToken
        );

        return EndpointResults.FromResult(result, httpContext);
    }

    public sealed record NearestPortsRequest(
        string? PickupAddress,
        decimal? Latitude,
        decimal? Longitude,
        IReadOnlyCollection<NearestPortCandidateRequest> Ports
    );

    public sealed record NearestPortCandidateRequest(
        Guid Id,
        string Name,
        string? Code,
        string? Country,
        decimal? Latitude,
        decimal? Longitude
    );
}
