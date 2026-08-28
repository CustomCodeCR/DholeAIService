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
    private const int MaximumCandidatesForAi = 80;

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
        if (!request.Latitude.HasValue || !request.Longitude.HasValue)
        {
            return Results.BadRequest(
                new
                {
                    code = "AI.Logistics.PickupCoordinatesRequired",
                    message = "Ubique primero el punto de recolección en el mapa para calcular los puertos cercanos.",
                }
            );
        }

        var radiusKm = Math.Clamp(
            request.MaxDistanceKm ?? MaximumAllowedRadiusKm,
            1m,
            MaximumAllowedRadiusKm
        );
        var pickupLatitude = request.Latitude.Value;
        var pickupLongitude = request.Longitude.Value;
        var pickupAddress = request.PickupAddress?.Trim();
        var transportMode = NormalizeTransportMode(request.TransportMode);

        var nominatim = httpClientFactory.CreateClient("nominatim");
        var overpass = httpClientFactory.CreateClient("overpass");
        var pickupLocation = await ResolvePickupLocationAsync(
            nominatim,
            pickupLatitude,
            pickupLongitude,
            cancellationToken
        );

        var discovered = new List<DiscoveredPort>();
        if (transportMode is not "Air" and not "Land")
        {
            discovered.AddRange(await DiscoverPortsWithOverpassAsync(
                overpass,
                pickupLatitude,
                pickupLongitude,
                radiusKm,
                cancellationToken
            ));
        }

        discovered.AddRange(await DiscoverNamedPortsWithNominatimAsync(
            nominatim,
            pickupAddress,
            pickupLocation,
            pickupLatitude,
            pickupLongitude,
            radiusKm,
            transportMode,
            cancellationToken
        ));
        var candidates = discovered
            .Where(port => !string.IsNullOrWhiteSpace(port.Name))
            .Select(port => new PortCandidate(
                port.Name.Trim(),
                NormalizeOptional(port.Code),
                NormalizeOptional(port.Country) ?? pickupLocation.Country,
                port.Latitude,
                port.Longitude,
                CalculateDistanceKm(
                    pickupLatitude,
                    pickupLongitude,
                    port.Latitude,
                    port.Longitude
                ),
                port.Source
            ))
            .Where(port => port.DistanceKm <= radiusKm)
            .GroupBy(
                port => $"{Normalize(port.Name)}|{Math.Round(port.Latitude, 3)}|{Math.Round(port.Longitude, 3)}",
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group.OrderBy(port => port.DistanceKm).First())
            .OrderBy(port => port.DistanceKm)
            .Take(MaximumCandidatesForAi)
            .ToArray();

        // No se devuelve un error cuando realmente no hay candidatos. El buscador geográfico
        // no depende del catálogo POL y la UI puede mostrar el estado vacío sin bloquear la tarifa.
        if (candidates.Length == 0)
        {
            return Results.Ok(new
            {
                content = "{\"recommendations\":[]}",
                maxDistanceKm = radiusKm,
                transportMode,
                pickup = new
                {
                    latitude = pickupLatitude,
                    longitude = pickupLongitude,
                    address = pickupAddress,
                },
            });
        }

        var candidatesJson = JsonSerializer.Serialize(
            candidates.Select((candidate, index) => new
            {
                id = $"geo-{index + 1}",
                candidate.Name,
                candidate.Code,
                candidate.Country,
                candidate.Latitude,
                candidate.Longitude,
                distanceKm = Math.Round(candidate.DistanceKm, 1),
                candidate.Source,
            })
        );

        var transportDescription = transportMode == "Air"
            ? "aeropuertos reales aptos para carga aérea"
            : transportMode == "Land"
                ? "nodos logísticos terrestres"
                : "puertos marítimos comerciales de carga internacional";

        var systemPrompt = string.Join(
            '\n',
            $"Eres un especialista en logística internacional y selección de {transportDescription}.",
            $"La modalidad seleccionada es {transportMode}. Solo recomienda infraestructura compatible con esa modalidad.",
            "La búsqueda se originó EXCLUSIVAMENTE desde las coordenadas de recolección indicadas por el usuario. No uses ni infieras el POL actual para calcular cercanía.",
            $"Los candidatos fueron descubiertos geográficamente alrededor de la recolección y filtrados matemáticamente a un radio máximo de {radiusKm:0} km.",
            "El catálogo POL de Dhole NO limita la búsqueda. Debes evaluar los puertos geográficos recibidos por distancia y viabilidad logística.",
            "Nunca inventes un punto que no esté en la lista de candidatos y no confundas nombres de ciudades, barrios o comercios con infraestructura logística real.",
            transportMode is "Maritime" or "Multimodal"
                ? "Para marítimo solo son válidos puertos comerciales que realmente manejen carga internacional: contenedores, carga general, graneles, RoRo de carga o terminales tanker. Excluye marinas, pesca, ferris o terminales solo de pasajeros, cruceros, muelles locales, astilleros, bases navales y puertos sin operación internacional de carga."
                : "Mantén estrictamente la infraestructura compatible con la modalidad seleccionada.",
            transportMode is "Maritime" or "Multimodal"
                ? "Si no puedes justificar que un candidato sirve para transporte marítimo internacional de carga, NO lo recomiendes."
                : "No recomiendes infraestructura de otra modalidad.",
            "Devuelve TODAS las opciones válidas de carga internacional que estén en los candidatos, hasta un máximo de 5. No omitas un puerto válido solo por preferir otro. Ordénalas estrictamente por distanceKm de menor a mayor. Conserva exactamente nombre, código, latitud, longitud y distancia del candidato elegido.",
            "Responde EXCLUSIVAMENTE JSON válido, sin markdown, con este formato:",
            "{\"recommendations\":[{\"name\":\"Qingdao Port\",\"code\":null,\"latitude\":36.0,\"longitude\":120.2,\"distanceKm\":12.3,\"reason\":\"explicación breve\"}]}",
            "Si no hay una opción útil, devuelve {\"recommendations\":[]} ."
        );

        var locationText = string.Join(
            " | ",
            new[]
            {
                pickupAddress,
                pickupLocation.Locality,
                pickupLocation.Region,
                pickupLocation.Country,
                $"coordenadas {pickupLatitude.ToString(CultureInfo.InvariantCulture)}, {pickupLongitude.ToString(CultureInfo.InvariantCulture)}",
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );

        var messages = new[]
        {
            new AiMessageRequest("system", systemPrompt),
            new AiMessageRequest(
                "user",
                $"Lugar de recolección: {locationText}\nModalidad: {transportMode}\nRadio máximo: {radiusKm:0} km\nCandidatos geográficos compatibles: {candidatesJson}"
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

    private static async Task<PickupLocation> ResolvePickupLocationAsync(
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
            if (!response.IsSuccessStatusCode) return PickupLocation.Empty;

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("address", out var address)) return PickupLocation.Empty;

            return new PickupLocation(
                ReadString(address, "country_code")?.ToUpperInvariant(),
                ReadString(address, "country"),
                FirstNonEmpty(
                    ReadString(address, "city"),
                    ReadString(address, "town"),
                    ReadString(address, "municipality"),
                    ReadString(address, "county"),
                    ReadString(address, "village")
                ),
                FirstNonEmpty(
                    ReadString(address, "state"),
                    ReadString(address, "province"),
                    ReadString(address, "region")
                )
            );
        }
        catch
        {
            return PickupLocation.Empty;
        }
    }

    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverPortsWithOverpassAsync(
        HttpClient client,
        decimal latitude,
        decimal longitude,
        decimal radiusKm,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var radiusMeters = Math.Clamp((int)Math.Ceiling(radiusKm * 1000m), 1000, 500000);
            var lat = latitude.ToString(CultureInfo.InvariantCulture);
            var lon = longitude.ToString(CultureInfo.InvariantCulture);
            var query = $"[out:json][timeout:25];(nwr(around:{radiusMeters},{lat},{lon})[\"industrial\"=\"port\"];nwr(around:{radiusMeters},{lat},{lon})[\"port\"=\"cargo\"];nwr(around:{radiusMeters},{lat},{lon})[\"cargo\"];nwr(around:{radiusMeters},{lat},{lon})[\"harbour\"];nwr(around:{radiusMeters},{lat},{lon})[\"seamark:type\"=\"harbour\"];nwr(around:{radiusMeters},{lat},{lon})[\"place\"=\"seaport\"];nwr(around:{radiusMeters},{lat},{lon})[\"seamark:harbour:category\"~\"cargo|container|bulk|tanker|roro\",i];nwr(around:{radiusMeters},{lat},{lon})[\"harbour:category\"~\"general|cargo|container|bulk|tanker|industrial|roro\",i];);out center tags;";

            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["data"] = query,
            });
            using var response = await client.PostAsync("interpreter", body, cancellationToken);
            if (!response.IsSuccessStatusCode) return Array.Empty<DiscoveredPort>();

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
            if (!document.RootElement.TryGetProperty("elements", out var elements)
                || elements.ValueKind != JsonValueKind.Array)
            {
                return Array.Empty<DiscoveredPort>();
            }

            var ports = new List<DiscoveredPort>();
            foreach (var element in elements.EnumerateArray())
            {
                if (!TryReadCoordinates(element, out var portLatitude, out var portLongitude)) continue;
                if (!element.TryGetProperty("tags", out var tags)) continue;

                var name = FirstNonEmpty(
                    ReadString(tags, "name:en"),
                    ReadString(tags, "name"),
                    ReadString(tags, "official_name"),
                    ReadString(tags, "seamark:name"),
                    ReadString(tags, "operator")
                );
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (!IsInternationalCargoPort(tags, name)) continue;

                ports.Add(new DiscoveredPort(
                    name,
                    FirstNonEmpty(ReadString(tags, "locode"), ReadString(tags, "seamark:harbour:locode"), ReadString(tags, "ref")),
                    ReadString(tags, "addr:country"),
                    portLatitude,
                    portLongitude,
                    "OpenStreetMap/Overpass"
                ));
            }

            return ports;
        }
        catch
        {
            return Array.Empty<DiscoveredPort>();
        }
    }

    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverNamedPortsWithNominatimAsync(
        HttpClient client,
        string? pickupAddress,
        PickupLocation location,
        decimal pickupLatitude,
        decimal pickupLongitude,
        decimal radiusKm,
        string transportMode,
        CancellationToken cancellationToken
    )
    {
        var addressLocality = pickupAddress?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var localities = new[] { location.Locality, addressLocality, location.Region, location.Country }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        if (localities.Length == 0 || transportMode == "Land") return Array.Empty<DiscoveredPort>();

        var countryFilter = string.IsNullOrWhiteSpace(location.CountryCode)
            ? string.Empty
            : $"&countrycodes={Uri.EscapeDataString(location.CountryCode.ToLowerInvariant())}";
        var results = new List<DiscoveredPort>();

        foreach (var locality in localities)
        {
            var queries = transportMode == "Air"
                ? new[]
                {
                    $"{locality} International Airport",
                    $"{locality} Airport",
                    $"Airport {locality}",
                    $"Aeropuerto {locality}",
                }
                : new[]
                {
                    $"{locality} cargo port",
                    $"{locality} container terminal",
                    $"{locality} commercial seaport",
                    $"Port of {locality} cargo",
                    $"Puerto de carga {locality}",
                };

            foreach (var query in queries)
            {
                try
                {
                    var url = $"search?format=jsonv2&limit=5&addressdetails=1&extratags=1{countryFilter}&q={Uri.EscapeDataString(query)}";
                    using var response = await client.GetAsync(url, cancellationToken);
                    if (!response.IsSuccessStatusCode) continue;

                    await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                    using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                    if (document.RootElement.ValueKind != JsonValueKind.Array) continue;

                    foreach (var row in document.RootElement.EnumerateArray())
                    {
                        var category = FirstNonEmpty(ReadString(row, "category"), ReadString(row, "class"))?.ToLowerInvariant();
                        var type = ReadString(row, "type")?.ToLowerInvariant();
                        JsonElement extraTags = default;
                        var hasExtraTags = row.TryGetProperty("extratags", out extraTags) && extraTags.ValueKind == JsonValueKind.Object;
                        var industrial = hasExtraTags ? ReadString(extraTags, "industrial")?.ToLowerInvariant() : null;
                        var harbour = hasExtraTags ? ReadString(extraTags, "harbour")?.ToLowerInvariant() : null;
                        var seamarkType = hasExtraTags ? ReadString(extraTags, "seamark:type")?.ToLowerInvariant() : null;

                        if (transportMode == "Air")
                        {
                            if (category != "aeroway" || (type != "aerodrome" && type != "airport")) continue;
                        }
                        else
                        {
                            var displayName = FirstNonEmpty(ReadString(row, "name"), ReadString(row, "display_name")) ?? string.Empty;
                            var explicitSeaport = category == "place" && type == "seaport";
                            if (hasExtraTags)
                            {
                                if (!IsInternationalCargoPort(extraTags, displayName, category, type)) continue;
                            }
                            else if (!explicitSeaport)
                            {
                                continue;
                            }
                        }
                        if (!TryParseCoordinate(row, "lat", out var latitude)
                            || !TryParseCoordinate(row, "lon", out var longitude))
                        {
                            continue;
                        }

                        var distance = CalculateDistanceKm(
                            pickupLatitude,
                            pickupLongitude,
                            latitude,
                            longitude
                        );
                        if (distance > radiusKm) continue;

                        var name = FirstNonEmpty(
                            ReadString(row, "name"),
                            ReadString(row, "display_name")?.Split(',').FirstOrDefault()
                        );
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var country = row.TryGetProperty("address", out var address)
                            ? ReadString(address, "country")
                            : location.Country;

                        results.Add(new DiscoveredPort(
                            name,
                            hasExtraTags
                                ? transportMode == "Air"
                                    ? FirstNonEmpty(ReadString(extraTags, "iata"), ReadString(extraTags, "icao"), ReadString(extraTags, "ref"))
                                    : FirstNonEmpty(ReadString(extraTags, "locode"), ReadString(extraTags, "seamark:harbour:locode"), ReadString(extraTags, "ref"))
                                : null,
                            country,
                            latitude,
                            longitude,
                            "OpenStreetMap/Nominatim"
                        ));
                    }
                }
                catch
                {
                    // Continúa con la siguiente variante de búsqueda.
                }
            }
        }

        return results;
    }

    private static bool IsInternationalCargoPort(
        JsonElement tags,
        string name,
        string? category = null,
        string? type = null
    )
    {
        var industrial = ReadString(tags, "industrial")?.ToLowerInvariant();
        var port = ReadString(tags, "port")?.ToLowerInvariant();
        var portType = ReadString(tags, "port:type")?.ToLowerInvariant();
        var cargo = ReadString(tags, "cargo")?.ToLowerInvariant();
        var seamarkCategory = ReadString(tags, "seamark:harbour:category")?.ToLowerInvariant();
        var harbourCategory = ReadString(tags, "harbour:category")?.ToLowerInvariant();
        var harbour = ReadString(tags, "harbour")?.ToLowerInvariant();
        var seamarkType = ReadString(tags, "seamark:type")?.ToLowerInvariant();
        var locode = FirstNonEmpty(
            ReadString(tags, "locode"),
            ReadString(tags, "seamark:harbour:locode")
        );
        var leisure = ReadString(tags, "leisure")?.ToLowerInvariant();
        var military = ReadString(tags, "military")?.ToLowerInvariant();
        var normalizedName = Normalize(name);

        static bool ContainsAny(string? value, params string[] tokens)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            return tokens.Any(token => value.Contains(token, StringComparison.OrdinalIgnoreCase));
        }

        var hasCargoSubtag = false;
        if (tags.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in tags.EnumerateObject())
            {
                if (!property.Name.StartsWith("cargo:", StringComparison.OrdinalIgnoreCase)) continue;
                var value = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : null;
                if (!string.Equals(value, "no", StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(value, "false", StringComparison.OrdinalIgnoreCase))
                {
                    hasCargoSubtag = true;
                    break;
                }
            }
        }

        var explicitCargo = port == "cargo"
            || (!string.IsNullOrWhiteSpace(cargo)
                && cargo is not "no" and not "passenger")
            || hasCargoSubtag
            || ContainsAny(seamarkCategory, "cargo", "container", "bulk", "tanker", "roro")
            || ContainsAny(harbourCategory, "general", "cargo", "container", "bulk", "tanker", "industrial", "roro");

        var clearlyNonCargo = leisure == "marina"
            || !string.IsNullOrWhiteSpace(military)
            || ContainsAny(port, "fishing", "passenger", "marina", "seaplane")
            || (!explicitCargo && ContainsAny(seamarkCategory, "fishing", "marina", "naval", "passenger", "shipyard", "ferry", "port_support"))
            || (!explicitCargo && ContainsAny(harbourCategory, "fishing", "marina", "military", "passenger", "shipyard", "ferry", "tourism", "yacht"))
            || (!explicitCargo && ContainsAny(normalizedName, "marina", "yacht", "fishing", "ferry terminal", "cruise terminal", "naval base", "shipyard"));

        if (clearlyNonCargo) return false;
        if (explicitCargo) return true;

        var maritimeInfrastructure = industrial == "port"
            || harbour == "yes"
            || seamarkType == "harbour"
            || portType is "seaport" or "deep_water"
            || category == "place" && type == "seaport"
            || type == "seaport"
            || ContainsAny(normalizedName, "container terminal", "cargo terminal", "commercial port", "commercial seaport", "puerto comercial", "puerto de carga");

        if (!maritimeInfrastructure) return false;

        // Discovery must not depend on complete OSM cargo subtags. Real international ports
        // can be tagged only as harbour/seaport. The AI performs the final cargo validation.
        return true;
    }

    private static bool TryReadCoordinates(
        JsonElement element,
        out decimal latitude,
        out decimal longitude
    )
    {
        if (TryReadNumber(element, "lat", out latitude)
            && TryReadNumber(element, "lon", out longitude))
        {
            return true;
        }

        if (element.TryGetProperty("center", out var center)
            && TryReadNumber(center, "lat", out latitude)
            && TryReadNumber(center, "lon", out longitude))
        {
            return true;
        }

        latitude = default;
        longitude = default;
        return false;
    }

    private static bool TryReadNumber(JsonElement element, string name, out decimal value)
    {
        value = default;
        if (!element.TryGetProperty(name, out var property)) return false;
        if (property.ValueKind == JsonValueKind.Number && property.TryGetDecimal(out value)) return true;
        return property.ValueKind == JsonValueKind.String
            && decimal.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryParseCoordinate(JsonElement element, string name, out decimal value)
    {
        value = default;
        if (!element.TryGetProperty(name, out var property)) return false;
        return decimal.TryParse(
            property.GetString(),
            NumberStyles.Float,
            CultureInfo.InvariantCulture,
            out value
        );
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property)) return null;
        return property.ValueKind == JsonValueKind.String
            ? NormalizeOptional(property.GetString())
            : null;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string NormalizeTransportMode(string? value)
    {
        var normalized = NormalizeOptional(value)?.ToLowerInvariant();
        return normalized switch
        {
            "air" or "aereo" or "aéreo" => "Air",
            "land" or "terrestre" => "Land",
            "multimodal" => "Multimodal",
            _ => "Maritime",
        };
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
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
            .ToLowerInvariant()
            .Trim();

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private sealed record PickupLocation(
        string? CountryCode,
        string? Country,
        string? Locality,
        string? Region
    )
    {
        public static PickupLocation Empty { get; } = new(null, null, null, null);
    }

    private sealed record DiscoveredPort(
        string Name,
        string? Code,
        string? Country,
        decimal Latitude,
        decimal Longitude,
        string Source
    );

    private sealed record PortCandidate(
        string Name,
        string? Code,
        string? Country,
        decimal Latitude,
        decimal Longitude,
        decimal DistanceKm,
        string Source
    );

    public sealed record NearestPortsRequest(
        string? PickupAddress,
        decimal? Latitude,
        decimal? Longitude,
        decimal? MaxDistanceKm = MaximumAllowedRadiusKm,
        string? TransportMode = "Maritime"
    );
}
