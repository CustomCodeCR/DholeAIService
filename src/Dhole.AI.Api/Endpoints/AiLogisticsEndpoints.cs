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
    private const decimal MaximumAllowedRadiusKm = 500m;

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
        if (!request.Latitude.HasValue || !request.Longitude.HasValue)
        {
            return Results.BadRequest(
                new
                {
                    code = "AI.Logistics.PickupCoordinatesRequired",
                    message = "Ubique primero el punto de recolección en el mapa para calcular puertos dentro del radio permitido.",
                }
            );
        }

        var requestedRadiusKm = request.MaxDistanceKm ?? MaximumAllowedRadiusKm;
        var radiusKm = Math.Clamp(requestedRadiusKm, 1m, MaximumAllowedRadiusKm);
        var pickupLatitude = request.Latitude.Value;
        var pickupLongitude = request.Longitude.Value;

        var ports = request.Ports
            .Where(port =>
                port.Id != Guid.Empty
                && !string.IsNullOrWhiteSpace(port.Name)
                && port.Latitude.HasValue
                && port.Longitude.HasValue
            )
            .Select(port => new
            {
                Port = port,
                DistanceKm = CalculateDistanceKm(
                    pickupLatitude,
                    pickupLongitude,
                    port.Latitude!.Value,
                    port.Longitude!.Value
                ),
            })
            .Where(candidate => candidate.DistanceKm <= radiusKm)
            .GroupBy(candidate => candidate.Port.Id)
            .Select(group => group.OrderBy(candidate => candidate.DistanceKm).First())
            .OrderBy(candidate => candidate.DistanceKm)
            .Take(120)
            .ToArray();

        if (ports.Length == 0)
        {
            return Results.BadRequest(
                new
                {
                    code = "AI.Logistics.NoPortsInsideRadius",
                    message = $"No existen POL configurados con coordenadas dentro de {radiusKm:0} km del punto de recolección.",
                    maxDistanceKm = radiusKm,
                }
            );
        }

        var candidatesJson = JsonSerializer.Serialize(
            ports.Select(candidate => new
            {
                id = candidate.Port.Id,
                name = candidate.Port.Name.Trim(),
                code = candidate.Port.Code?.Trim(),
                country = candidate.Port.Country?.Trim(),
                latitude = candidate.Port.Latitude,
                longitude = candidate.Port.Longitude,
                distanceKm = Math.Round(candidate.DistanceKm, 1),
            })
        );

        var systemPrompt = $"""
            Eres un especialista en logística internacional. Debes recomendar únicamente puertos incluidos en la lista de candidatos recibida.
            Todos los candidatos ya fueron filtrados matemáticamente para estar a un máximo de {radiusKm:0} km del punto EXW.
            Usa distancia, viabilidad logística y cercanía para ordenar las opciones. Nunca inventes puertos, IDs ni datos fuera de la lista.
            Devuelve máximo 3 opciones ordenadas de mejor a peor y conserva la distancia recibida para cada puerto.
            Responde EXCLUSIVAMENTE JSON válido, sin markdown, con este formato:
            {{"recommendations":[{{"portId":"GUID","distanceKm":123.4,"reason":"explicación breve"}}]}}
            Si no puedes determinar una opción con suficiente confianza, devuelve {{"recommendations":[]}}.
            """;

        var locationText = $"{pickupAddress ?? "Dirección no indicada"} | coordenadas {pickupLatitude}, {pickupLongitude}";

        var messages = new[]
        {
            new AiMessageRequest("system", systemPrompt),
            new AiMessageRequest(
                "user",
                $"Lugar de recolección: {locationText}\nRadio máximo: {radiusKm:0} km\nPuertos candidatos configurados en Dhole: {candidatesJson}"
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

    private static decimal CalculateDistanceKm(
        decimal latitude1,
        decimal longitude1,
        decimal latitude2,
        decimal longitude2
    )
    {
        const double earthRadiusKm = 6371.0088d;
        var lat1 = DegreesToRadians((double)latitude1);
        var lat2 = DegreesToRadians((double)latitude2);
        var deltaLat = DegreesToRadians((double)(latitude2 - latitude1));
        var deltaLon = DegreesToRadians((double)(longitude2 - longitude1));

        var a = Math.Sin(deltaLat / 2d) * Math.Sin(deltaLat / 2d)
            + Math.Cos(lat1) * Math.Cos(lat2)
            * Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return (decimal)(earthRadiusKm * c);
    }

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    public sealed record NearestPortsRequest(
        string? PickupAddress,
        decimal? Latitude,
        decimal? Longitude,
        IReadOnlyCollection<NearestPortCandidateRequest> Ports,
        decimal? MaxDistanceKm = MaximumAllowedRadiusKm
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
