using System.Globalization;
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
    private const int MaximumGeocodeAttempts = 30;

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
        IHttpClientFactory httpClientFactory,
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
        var nominatim = httpClientFactory.CreateClient("nominatim");

        var pickupCountry = await ResolvePickupCountryAsync(
            nominatim,
            pickupLatitude,
            pickupLongitude,
            cancellationToken
        );

        var configuredPorts = request.Ports
            .Where(port => port.Id != Guid.Empty && !string.IsNullOrWhiteSpace(port.Name))
            .GroupBy(port => port.Id)
            .Select(group => group.First())
            .ToArray();

        var resolvedPorts = new List<ResolvedPortCandidate>(configuredPorts.Length);
        var geocodeAttempts = 0;

        foreach (var port in configuredPorts)
        {
            if (port.Latitude.HasValue && port.Longitude.HasValue)
            {
                resolvedPorts.Add(new ResolvedPortCandidate(
                    port,
                    port.Latitude.Value,
                    port.Longitude.Value
                ));
                continue;
            }

            if (!CouldBelongToPickupCountry(port, pickupCountry)) continue;
            if (geocodeAttempts >= MaximumGeocodeAttempts) continue;

            geocodeAttempts += 1;
            var coordinates = await ResolvePortCoordinatesAsync(
                nominatim,
                port,
                pickupCountry,
                cancellationToken
            );

            if (coordinates is not null)
            {
                resolvedPorts.Add(new ResolvedPortCandidate(
                    port,
                    coordinates.Value.Latitude,
                    coordinates.Value.Longitude
                ));
            }
        }

        var ports = resolvedPorts
            .Select(candidate => new
            {
                Candidate = candidate,
                DistanceKm = CalculateDistanceKm(
                    pickupLatitude,
                    pickupLongitude,
                    candidate.Latitude,
                    candidate.Longitude
                ),
            })
            .Where(candidate => candidate.DistanceKm <= radiusKm)
            .GroupBy(candidate => candidate.Candidate.Port.Id)
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
                    message = $"No existen puertos configurados que puedan resolverse dentro de {radiusKm:0} km del punto de recolección.",
                    maxDistanceKm = radiusKm,
                    pickupCountry = pickupCountry.Name,
                }
            );
        }

        var candidatesJson = JsonSerializer.Serialize(
            ports.Select(candidate => new
            {
                id = candidate.Candidate.Port.Id,
                name = candidate.Candidate.Port.Name.Trim(),
                code = candidate.Candidate.Port.Code?.Trim(),
                country = candidate.Candidate.Port.Country?.Trim(),
                latitude = candidate.Candidate.Latitude,
                longitude = candidate.Candidate.Longitude,
                distanceKm = Math.Round(candidate.DistanceKm, 1),
            })
        );

        var systemPrompt = $"""
            Eres un especialista en logística internacional. Debes recomendar únicamente puertos incluidos en la lista de candidatos recibida.
            El punto de referencia es EXCLUSIVAMENTE la ubicación de recolección marcada por el usuario, no el POL que estuviera seleccionado previamente.
            Todos los candidatos ya fueron filtrados matemáticamente para estar a un máximo de {radiusKm:0} km del punto EXW.
            Usa distancia, viabilidad logística y cercanía para ordenar las opciones. Nunca inventes puertos, IDs ni datos fuera de la lista.
            Devuelve máximo 3 opciones ordenadas de mejor a peor y conserva exactamente la distancia recibida para cada puerto.
            La interfaz permitirá al usuario cambiar el POL a una de estas opciones; no asumas que el POL actual debe conservarse.
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

    private static async Task<PickupCountry> ResolvePickupCountryAsync(
        HttpClient client,
        decimal latitude,
        decimal longitude,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var url = $"reverse?format=jsonv2&addressdetails=1&lat={latitude.ToString(CultureInfo.InvariantCulture)}&lon={longitude.ToString(CultureInfo.InvariantCulture)}";
            using var response = await client.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode) return PickupCountry.Empty;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("address", out var address)) return PickupCountry.Empty;

            var code = address.TryGetProperty("country_code", out var countryCode)
                ? countryCode.GetString()?.Trim().ToUpperInvariant()
                : null;
            var name = address.TryGetProperty("country", out var countryName)
                ? countryName.GetString()?.Trim()
                : null;

            return new PickupCountry(code, name);
        }
        catch
        {
            return PickupCountry.Empty;
        }
    }

    private static bool CouldBelongToPickupCountry(
        NearestPortCandidateRequest port,
        PickupCountry pickupCountry
    )
    {
        if (string.IsNullOrWhiteSpace(pickupCountry.Code) && string.IsNullOrWhiteSpace(pickupCountry.Name))
            return true;

        var configuredCountry = port.Country?.Trim();
        if (!string.IsNullOrWhiteSpace(configuredCountry))
        {
            if (!string.IsNullOrWhiteSpace(pickupCountry.Code)
                && string.Equals(configuredCountry, pickupCountry.Code, StringComparison.OrdinalIgnoreCase))
                return true;

            if (!string.IsNullOrWhiteSpace(pickupCountry.Name)
                && Normalize(configuredCountry).Contains(Normalize(pickupCountry.Name)))
                return true;

            return false;
        }

        if (!string.IsNullOrWhiteSpace(pickupCountry.Name)
            && Normalize(port.Name).Contains(Normalize(pickupCountry.Name)))
            return true;

        // Si el catálogo no trae país explícito, todavía se intenta resolver el puerto
        // restringiendo Nominatim al país de la recolección.
        return !port.Name.Contains(',');
    }

    private static async Task<(decimal Latitude, decimal Longitude)?> ResolvePortCoordinatesAsync(
        HttpClient client,
        NearestPortCandidateRequest port,
        PickupCountry pickupCountry,
        CancellationToken cancellationToken
    )
    {
        var countryFilter = string.IsNullOrWhiteSpace(pickupCountry.Code)
            ? string.Empty
            : $"&countrycodes={Uri.EscapeDataString(pickupCountry.Code.ToLowerInvariant())}";

        var queries = new[]
        {
            port.Name.Trim(),
            $"Puerto {port.Name.Trim()}",
            $"Port {port.Name.Trim()}",
        }.Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var query in queries)
        {
            try
            {
                var url = $"search?format=jsonv2&limit=1&addressdetails=1{countryFilter}&q={Uri.EscapeDataString(query)}";
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.ValueKind != JsonValueKind.Array
                    || document.RootElement.GetArrayLength() == 0)
                    continue;

                var first = document.RootElement[0];
                if (!first.TryGetProperty("lat", out var latitudeElement)
                    || !first.TryGetProperty("lon", out var longitudeElement))
                    continue;

                if (!decimal.TryParse(latitudeElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                    || !decimal.TryParse(longitudeElement.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude))
                    continue;

                return (latitude, longitude);
            }
            catch
            {
                // Intenta la siguiente variante del nombre del puerto.
            }
        }

        return null;
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

    private static string Normalize(string value) =>
        string.Concat(value.Normalize(System.Text.NormalizationForm.FormD)
            .Where(character => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(character) != System.Globalization.UnicodeCategory.NonSpacingMark))
            .ToLowerInvariant()
            .Trim();

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private sealed record PickupCountry(string? Code, string? Name)
    {
        public static PickupCountry Empty { get; } = new(null, null);
    }

    private sealed record ResolvedPortCandidate(
        NearestPortCandidateRequest Port,
        decimal Latitude,
        decimal Longitude
    );

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
