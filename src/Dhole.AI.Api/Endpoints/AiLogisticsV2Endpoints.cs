using System.Globalization;
using System.Text;
using System.Text.Json;
using CustomCodeFramework.Cqrs.Dispatching;
using Dhole.AI.Api.Authorization;
using Dhole.AI.Api.Extensions;
using Dhole.AI.Application.Features.Executions.ExecuteChat;
using Dhole.AI.Contracts.Executions.Request;

namespace Dhole.AI.Api.Endpoints;

/// <summary>
/// Resilient nearest-port lookup used by the EXW map.
/// Geographic discovery is deterministic; AI only enriches the explanation and is never
/// allowed to suppress valid geographic candidates.
/// </summary>
public static class AiLogisticsV2Endpoints
{
    private const string PricingWorkspaceScope = "pricing.workspace.access";
    private const string ProfileKey = "pricing-dashboard-analysis";
    private const decimal MaximumAllowedRadiusKm = 500m;
    private const int MaximumRecommendations = 5;
    private const int MaximumCandidates = 80;

    private static readonly IReadOnlyDictionary<string, KnownPort[]> KnownPortsByCountry =
        new Dictionary<string, KnownPort[]>(StringComparer.OrdinalIgnoreCase)
        {
            // Safety-net entries backed by real international cargo ports. They do not replace
            // global discovery; they guarantee service when public OSM endpoints are degraded.
            ["CR"] =
            [
                new("Puerto Caldera", "CRCAL", "Costa Rica", 9.9110m, -84.7220m),
                new("Puerto Moín", "CRPMN", "Costa Rica", 10.0060m, -83.0820m),
                new("Puerto Limón", "CRLIO", "Costa Rica", 9.9930m, -83.0360m),
            ],
        };

    public static IEndpointRouteBuilder MapAiLogisticsV2Endpoints(this IEndpointRouteBuilder app)
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
            return Results.BadRequest(new
            {
                code = "AI.Logistics.PickupCoordinatesRequired",
                message = "Ubique primero el punto de recolección en el mapa para calcular los puertos cercanos.",
            });
        }

        var pickupLatitude = request.Latitude.Value;
        var pickupLongitude = request.Longitude.Value;
        var radiusKm = Math.Clamp(request.MaxDistanceKm ?? MaximumAllowedRadiusKm, 1m, MaximumAllowedRadiusKm);
        var transportMode = NormalizeTransportMode(request.TransportMode);
        var pickupAddress = NormalizeOptional(request.PickupAddress);

        var nominatim = httpClientFactory.CreateClient("nominatim");
        var pickupLocation = await ResolvePickupLocationAsync(
            nominatim,
            pickupLatitude,
            pickupLongitude,
            cancellationToken
        );

        var discovered = new List<DiscoveredPort>();

        if (transportMode is "Maritime" or "Multimodal")
        {
            AddKnownCountryPorts(
                discovered,
                pickupLocation.CountryCode,
                pickupLatitude,
                pickupLongitude,
                radiusKm
            );

            foreach (var clientName in new[] { "overpass", "overpass-kumi" })
            {
                var overpass = httpClientFactory.CreateClient(clientName);
                var overpassPorts = await DiscoverMaritimePortsWithOverpassAsync(
                    overpass,
                    pickupLatitude,
                    pickupLongitude,
                    radiusKm,
                    cancellationToken
                );
                discovered.AddRange(overpassPorts);

                if (BuildCandidates(discovered, pickupLocation.Country, pickupLatitude, pickupLongitude, radiusKm).Length >= MaximumRecommendations)
                {
                    break;
                }
            }
        }

        // Nominatim is a deliberately small fallback. Public Nominatim is not used as a bulk
        // port database and therefore is only queried when the primary discovery did not find
        // enough usable results.
        if (BuildCandidates(discovered, pickupLocation.Country, pickupLatitude, pickupLongitude, radiusKm).Length < MaximumRecommendations)
        {
            discovered.AddRange(await DiscoverWithNominatimFallbackAsync(
                nominatim,
                pickupLocation,
                pickupLatitude,
                pickupLongitude,
                radiusKm,
                transportMode,
                cancellationToken
            ));
        }

        var candidates = BuildCandidates(
            discovered,
            pickupLocation.Country,
            pickupLatitude,
            pickupLongitude,
            radiusKm
        );

        if (candidates.Length == 0)
        {
            return EndpointResults.Ok(new
            {
                content = "{\"recommendations\":[]}",
                maxDistanceKm = radiusKm,
                transportMode,
                discovery = "geographic-v2",
            });
        }

        var selected = candidates.Take(MaximumRecommendations).ToArray();
        var fallbackContent = BuildRecommendationsJson(selected, null);

        // The model is intentionally not a gate. It can improve the explanatory reason, but
        // distance, coordinates, identity and inclusion of a valid port remain deterministic.
        try
        {
            var aiResult = await EnrichReasonsWithAiAsync(
                dispatcher,
                selected,
                pickupAddress,
                pickupLocation,
                pickupLatitude,
                pickupLongitude,
                transportMode,
                radiusKm,
                httpContext,
                cancellationToken
            );

            if (aiResult.IsSuccess)
            {
                var content = BuildRecommendationsJson(selected, aiResult.Value.Content);
                return EndpointResults.Ok(aiResult.Value with { Content = content });
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // Provider timeout must not erase ports already found by geographic discovery.
        }
        catch
        {
            // AI enrichment is best-effort. The geographic result remains valid and usable.
        }

        return EndpointResults.Ok(new
        {
            content = fallbackContent,
            maxDistanceKm = radiusKm,
            transportMode,
            discovery = "geographic-v2-deterministic-fallback",
        });
    }

    private static async Task<CustomCodeFramework.Core.Results.Result<Dhole.AI.Contracts.Executions.Response.AiChatResultDto>> EnrichReasonsWithAiAsync(
        ICommandDispatcher dispatcher,
        IReadOnlyCollection<PortCandidate> selected,
        string? pickupAddress,
        PickupLocation pickupLocation,
        decimal pickupLatitude,
        decimal pickupLongitude,
        string transportMode,
        decimal radiusKm,
        HttpContext httpContext,
        CancellationToken cancellationToken
    )
    {
        var candidatePayload = selected.Select((candidate, index) => new
        {
            id = $"geo-{index + 1}",
            candidate.Name,
            candidate.Code,
            candidate.Country,
            latitude = candidate.Latitude,
            longitude = candidate.Longitude,
            distanceKm = Math.Round(candidate.DistanceKm, 1),
            candidate.Source,
        });

        var locationText = string.Join(
            " | ",
            new[]
            {
                pickupAddress,
                pickupLocation.Locality,
                pickupLocation.Region,
                pickupLocation.Country,
                $"{pickupLatitude.ToString(CultureInfo.InvariantCulture)}, {pickupLongitude.ToString(CultureInfo.InvariantCulture)}",
            }.Where(value => !string.IsNullOrWhiteSpace(value))
        );

        var systemPrompt = """
            Actúa como analista senior de logística internacional. Recibirás puertos ya descubiertos,
            validados y ordenados matemáticamente por el backend. NO elimines, agregues, renombres,
            reordenes ni cambies coordenadas/distancias. Tu única tarea es escribir una razón logística
            breve y útil para cada id. Devuelve exclusivamente JSON válido con esta forma:
            {"reasons":[{"id":"geo-1","reason":"explicación breve"}]}
            Devuelve una razón para cada id recibido. No uses markdown.
            """;

        var messages = new[]
        {
            new AiMessageRequest("system", systemPrompt),
            new AiMessageRequest(
                "user",
                $"Recolección: {locationText}\nModalidad: {transportMode}\nRadio: {radiusKm:0} km\nPuertos: {JsonSerializer.Serialize(candidatePayload)}"
            ),
        };

        return await dispatcher.DispatchAsync(
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
    }

    private static PortCandidate[] BuildCandidates(
        IEnumerable<DiscoveredPort> discovered,
        string? fallbackCountry,
        decimal pickupLatitude,
        decimal pickupLongitude,
        decimal radiusKm
    )
    {
        return discovered
            .Where(port => !string.IsNullOrWhiteSpace(port.Name))
            .Select(port => new PortCandidate(
                port.Name.Trim(),
                NormalizeOptional(port.Code),
                NormalizeOptional(port.Country) ?? fallbackCountry,
                port.Latitude,
                port.Longitude,
                CalculateDistanceKm(pickupLatitude, pickupLongitude, port.Latitude, port.Longitude),
                port.Source,
                port.Confidence
            ))
            .Where(port => port.DistanceKm <= radiusKm && port.Confidence >= 70)
            .GroupBy(
                port => BuildPortIdentity(port.Name, port.Code, port.Latitude, port.Longitude),
                StringComparer.OrdinalIgnoreCase
            )
            .Select(group => group
                .OrderByDescending(port => port.Confidence)
                .ThenBy(port => port.DistanceKm)
                .First())
            .OrderBy(port => port.DistanceKm)
            .ThenByDescending(port => port.Confidence)
            .Take(MaximumCandidates)
            .ToArray();
    }

    private static string BuildRecommendationsJson(
        IReadOnlyList<PortCandidate> candidates,
        string? aiContent
    )
    {
        var reasons = ParseAiReasons(aiContent);
        var recommendations = candidates.Select((candidate, index) =>
        {
            var id = $"geo-{index + 1}";
            var reason = reasons.TryGetValue(id, out var aiReason) && !string.IsNullOrWhiteSpace(aiReason)
                ? aiReason.Trim()
                : $"Puerto comercial de carga internacional detectado a {Math.Round(candidate.DistanceKm, 1):0.0} km del punto de recolección.";

            return new
            {
                name = candidate.Name,
                code = candidate.Code,
                latitude = candidate.Latitude,
                longitude = candidate.Longitude,
                distanceKm = Math.Round(candidate.DistanceKm, 1),
                reason,
            };
        });

        return JsonSerializer.Serialize(new { recommendations });
    }

    private static Dictionary<string, string> ParseAiReasons(string? content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(content)) return result;

        try
        {
            var cleaned = content.Trim();
            if (cleaned.StartsWith("```", StringComparison.Ordinal))
            {
                var firstNewLine = cleaned.IndexOf('\n');
                var lastFence = cleaned.LastIndexOf("```", StringComparison.Ordinal);
                if (firstNewLine >= 0 && lastFence > firstNewLine)
                {
                    cleaned = cleaned[(firstNewLine + 1)..lastFence].Trim();
                }
            }

            using var document = JsonDocument.Parse(cleaned);
            if (!document.RootElement.TryGetProperty("reasons", out var reasons)
                || reasons.ValueKind != JsonValueKind.Array)
            {
                return result;
            }

            foreach (var item in reasons.EnumerateArray())
            {
                var id = ReadString(item, "id");
                var reason = ReadString(item, "reason");
                if (!string.IsNullOrWhiteSpace(id) && !string.IsNullOrWhiteSpace(reason))
                {
                    result[id] = reason;
                }
            }
        }
        catch
        {
            // Invalid model JSON simply means deterministic reasons are used.
        }

        return result;
    }

    private static void AddKnownCountryPorts(
        ICollection<DiscoveredPort> destination,
        string? countryCode,
        decimal pickupLatitude,
        decimal pickupLongitude,
        decimal radiusKm
    )
    {
        if (string.IsNullOrWhiteSpace(countryCode)
            || !KnownPortsByCountry.TryGetValue(countryCode, out var knownPorts))
        {
            return;
        }

        foreach (var port in knownPorts)
        {
            if (CalculateDistanceKm(pickupLatitude, pickupLongitude, port.Latitude, port.Longitude) > radiusKm)
            {
                continue;
            }

            destination.Add(new DiscoveredPort(
                port.Name,
                port.Code,
                port.Country,
                port.Latitude,
                port.Longitude,
                "Verified cargo-port safety net",
                100
            ));
        }
    }

    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverMaritimePortsWithOverpassAsync(
        HttpClient client,
        decimal pickupLatitude,
        decimal pickupLongitude,
        decimal radiusKm,
        CancellationToken cancellationToken
    )
    {
        var ports = new List<DiscoveredPort>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var radii = new[] { Math.Min(radiusKm, 125m), Math.Min(radiusKm, 300m), radiusKm }
            .Where(value => value >= 1m)
            .Distinct()
            .OrderBy(value => value)
            .ToArray();

        foreach (var searchRadiusKm in radii)
        {
            try
            {
                var radiusMeters = Math.Clamp((int)Math.Ceiling(searchRadiusKm * 1000m), 1000, 500000);
                var lat = pickupLatitude.ToString(CultureInfo.InvariantCulture);
                var lon = pickupLongitude.ToString(CultureInfo.InvariantCulture);
                var query = $"[out:json][timeout:25];(nwr(around:{radiusMeters},{lat},{lon})[\"industrial\"=\"port\"];nwr(around:{radiusMeters},{lat},{lon})[\"harbour\"=\"yes\"];nwr(around:{radiusMeters},{lat},{lon})[\"port\"=\"cargo\"];nwr(around:{radiusMeters},{lat},{lon})[\"port\"=\"seaport\"];nwr(around:{radiusMeters},{lat},{lon})[\"landuse\"=\"harbour\"];nwr(around:{radiusMeters},{lat},{lon})[\"place\"=\"seaport\"];nwr(around:{radiusMeters},{lat},{lon})[\"seamark:type\"=\"harbour\"];nwr(around:{radiusMeters},{lat},{lon})[\"seamark:harbour:category\"~\"cargo|container|bulk|tanker|roro\",i];nwr(around:{radiusMeters},{lat},{lon})[\"harbour:category\"~\"general|cargo|container|bulk|tanker|industrial|roro\",i];);out center tags;";

                using var body = new FormUrlEncodedContent(new Dictionary<string, string> { ["data"] = query });
                using var response = await client.PostAsync("interpreter", body, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (!document.RootElement.TryGetProperty("elements", out var elements)
                    || elements.ValueKind != JsonValueKind.Array)
                {
                    continue;
                }

                foreach (var element in elements.EnumerateArray())
                {
                    if (!TryReadCoordinates(element, out var latitude, out var longitude)) continue;
                    if (!element.TryGetProperty("tags", out var tags) || tags.ValueKind != JsonValueKind.Object) continue;

                    var name = FirstNonEmpty(
                        ReadString(tags, "name:en"),
                        ReadString(tags, "name"),
                        ReadString(tags, "official_name"),
                        ReadString(tags, "seamark:name"),
                        ReadString(tags, "operator")
                    );
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var confidence = CargoPortConfidence(tags, name);
                    if (confidence < 70) continue;
                    if (CalculateDistanceKm(pickupLatitude, pickupLongitude, latitude, longitude) > radiusKm) continue;

                    var code = FirstNonEmpty(
                        ReadString(tags, "locode"),
                        ReadString(tags, "seamark:harbour:locode"),
                        ReadString(tags, "ref")
                    );
                    var key = BuildPortIdentity(name, code, latitude, longitude);
                    if (!seen.Add(key)) continue;

                    ports.Add(new DiscoveredPort(
                        name,
                        code,
                        ReadString(tags, "addr:country"),
                        latitude,
                        longitude,
                        "OpenStreetMap/Overpass",
                        confidence
                    ));
                }

                if (ports.Count >= MaximumRecommendations) break;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Try the next radius/provider.
            }
            catch
            {
                // Public Overpass endpoints can throttle or fail. A second provider and the
                // country safety-net prevent a false empty result.
            }
        }

        return ports;
    }

    private static int CargoPortConfidence(JsonElement tags, string name)
    {
        var normalizedName = Normalize(name);
        var industrial = Normalize(ReadString(tags, "industrial") ?? string.Empty);
        var port = Normalize(ReadString(tags, "port") ?? string.Empty);
        var portType = Normalize(ReadString(tags, "port:type") ?? string.Empty);
        var cargo = Normalize(ReadString(tags, "cargo") ?? string.Empty);
        var harbour = Normalize(ReadString(tags, "harbour") ?? string.Empty);
        var landuse = Normalize(ReadString(tags, "landuse") ?? string.Empty);
        var place = Normalize(ReadString(tags, "place") ?? string.Empty);
        var seamarkType = Normalize(ReadString(tags, "seamark:type") ?? string.Empty);
        var categories = Normalize(string.Join(' ',
            ReadString(tags, "seamark:harbour:category"),
            ReadString(tags, "harbour:category"),
            ReadString(tags, "terminal"),
            ReadString(tags, "cargo:type")));
        var locode = FirstNonEmpty(ReadString(tags, "locode"), ReadString(tags, "seamark:harbour:locode"));

        var forbidden = string.Join(' ',
            ReadString(tags, "leisure"),
            ReadString(tags, "military"),
            ReadString(tags, "ferry"),
            categories,
            normalizedName).ToLowerInvariant();

        var explicitCargo = port == "cargo"
            || cargo is "yes" or "general" or "container" or "containers"
            || ContainsAny(categories, "cargo", "container", "bulk", "tanker", "roro", "industrial");

        if (!explicitCargo && ContainsAny(forbidden,
                "marina", "yacht", "fishing", "fishery", "passenger", "cruise", "ferry", "naval", "military"))
        {
            return 0;
        }

        if (explicitCargo) return 95;
        if (!string.IsNullOrWhiteSpace(locode)) return 90;
        if (industrial == "port") return 88;
        if (portType is "seaport" or "deep_water" || port == "seaport") return 85;
        if (place == "seaport") return 82;
        if (ContainsAny(normalizedName, "container terminal", "cargo terminal", "commercial port", "commercial seaport", "puerto comercial", "puerto de carga")) return 80;
        if (landuse == "harbour" || seamarkType == "harbour" || harbour == "yes") return 72;
        return 0;
    }

    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverWithNominatimFallbackAsync(
        HttpClient client,
        PickupLocation location,
        decimal pickupLatitude,
        decimal pickupLongitude,
        decimal radiusKm,
        string transportMode,
        CancellationToken cancellationToken
    )
    {
        if (transportMode == "Land") return Array.Empty<DiscoveredPort>();

        var queries = transportMode == "Air"
            ? new[] { "international cargo airport", "international airport" }
            : new[] { "cargo port", "seaport" };
        var results = new List<DiscoveredPort>();
        var viewBox = BuildGeographicViewBox(pickupLatitude, pickupLongitude, radiusKm);

        for (var index = 0; index < queries.Length; index++)
        {
            if (index > 0) await Task.Delay(TimeSpan.FromMilliseconds(1100), cancellationToken);

            try
            {
                var url = $"search?format=jsonv2&limit=40&addressdetails=1&extratags=1&bounded=1&viewbox={Uri.EscapeDataString(viewBox)}&q={Uri.EscapeDataString(queries[index])}";
                using var response = await client.GetAsync(url, cancellationToken);
                if (!response.IsSuccessStatusCode) continue;

                await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
                using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
                if (document.RootElement.ValueKind != JsonValueKind.Array) continue;

                foreach (var row in document.RootElement.EnumerateArray())
                {
                    if (!TryParseCoordinate(row, "lat", out var latitude)
                        || !TryParseCoordinate(row, "lon", out var longitude)) continue;
                    if (CalculateDistanceKm(pickupLatitude, pickupLongitude, latitude, longitude) > radiusKm) continue;

                    var category = FirstNonEmpty(ReadString(row, "category"), ReadString(row, "class"))?.ToLowerInvariant();
                    var type = ReadString(row, "type")?.ToLowerInvariant();
                    var name = FirstNonEmpty(ReadString(row, "name"), ReadString(row, "display_name")?.Split(',').FirstOrDefault());
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var confidence = 0;
                    JsonElement extraTags = default;
                    var hasExtraTags = row.TryGetProperty("extratags", out extraTags) && extraTags.ValueKind == JsonValueKind.Object;

                    if (transportMode == "Air")
                    {
                        if (category != "aeroway" || type is not ("aerodrome" or "airport")) continue;
                        confidence = 80;
                    }
                    else
                    {
                        confidence = hasExtraTags
                            ? CargoPortConfidence(extraTags, name)
                            : category == "place" && type == "seaport" ? 82 : 0;
                        if (confidence < 70) continue;
                    }

                    var code = hasExtraTags
                        ? transportMode == "Air"
                            ? FirstNonEmpty(ReadString(extraTags, "iata"), ReadString(extraTags, "icao"), ReadString(extraTags, "ref"))
                            : FirstNonEmpty(ReadString(extraTags, "locode"), ReadString(extraTags, "seamark:harbour:locode"), ReadString(extraTags, "ref"))
                        : null;
                    var country = row.TryGetProperty("address", out var address)
                        ? ReadString(address, "country")
                        : location.Country;

                    results.Add(new DiscoveredPort(
                        name,
                        code,
                        country,
                        latitude,
                        longitude,
                        "OpenStreetMap/Nominatim",
                        confidence
                    ));
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Continue to the deterministic result already discovered.
            }
            catch
            {
                // Best-effort fallback.
            }
        }

        return results;
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
                FirstNonEmpty(ReadString(address, "state"), ReadString(address, "province"), ReadString(address, "region"))
            );
        }
        catch
        {
            return PickupLocation.Empty;
        }
    }

    private static string BuildGeographicViewBox(decimal latitude, decimal longitude, decimal radiusKm)
    {
        const double kmPerLatitudeDegree = 111.32d;
        var lat = (double)latitude;
        var lon = (double)longitude;
        var radius = (double)radiusKm;
        var latDelta = radius / kmPerLatitudeDegree;
        var lonKmPerDegree = kmPerLatitudeDegree * Math.Max(Math.Abs(Math.Cos(DegreesToRadians(lat))), 0.05d);
        var lonDelta = Math.Min(radius / lonKmPerDegree, 180d);

        return string.Join(",",
            Math.Max(-180d, lon - lonDelta).ToString("0.######", CultureInfo.InvariantCulture),
            Math.Min(90d, lat + latDelta).ToString("0.######", CultureInfo.InvariantCulture),
            Math.Min(180d, lon + lonDelta).ToString("0.######", CultureInfo.InvariantCulture),
            Math.Max(-90d, lat - latDelta).ToString("0.######", CultureInfo.InvariantCulture));
    }

    private static string BuildPortIdentity(string name, string? code, decimal latitude, decimal longitude)
    {
        if (!string.IsNullOrWhiteSpace(code)) return $"code:{Normalize(code)}";
        return $"{Normalize(name)}|{Math.Round(latitude, 2)}|{Math.Round(longitude, 2)}";
    }

    private static bool TryReadCoordinates(JsonElement element, out decimal latitude, out decimal longitude)
    {
        if (TryReadNumber(element, "lat", out latitude) && TryReadNumber(element, "lon", out longitude)) return true;
        if (element.TryGetProperty("center", out var center)
            && TryReadNumber(center, "lat", out latitude)
            && TryReadNumber(center, "lon", out longitude)) return true;

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
        return property.ValueKind switch
        {
            JsonValueKind.Number => property.TryGetDecimal(out value),
            JsonValueKind.String => decimal.TryParse(property.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value),
            _ => false,
        };
    }

    private static string? ReadString(JsonElement element, string name)
    {
        if (!element.TryGetProperty(name, out var property) || property.ValueKind != JsonValueKind.String) return null;
        return NormalizeOptional(property.GetString());
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
            + Math.Cos(lat1) * Math.Cos(lat2) * Math.Sin(deltaLon / 2d) * Math.Sin(deltaLon / 2d);
        var c = 2d * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1d - a));
        return (decimal)(earthRadiusKm * c);
    }

    private static string Normalize(string value) =>
        string.Concat(value.Normalize(NormalizationForm.FormD)
            .Where(character => CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark))
            .ToLowerInvariant()
            .Trim();

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.OrdinalIgnoreCase));

    private static double DegreesToRadians(double degrees) => degrees * Math.PI / 180d;

    private sealed record PickupLocation(string? CountryCode, string? Country, string? Locality, string? Region)
    {
        public static PickupLocation Empty { get; } = new(null, null, null, null);
    }

    private sealed record KnownPort(string Name, string Code, string Country, decimal Latitude, decimal Longitude);

    private sealed record DiscoveredPort(
        string Name,
        string? Code,
        string? Country,
        decimal Latitude,
        decimal Longitude,
        string Source,
        int Confidence
    );

    private sealed record PortCandidate(
        string Name,
        string? Code,
        string? Country,
        decimal Latitude,
        decimal Longitude,
        decimal DistanceKm,
        string Source,
        int Confidence
    );

    public sealed record NearestPortsRequest(
        string? PickupAddress,
        decimal? Latitude,
        decimal? Longitude,
        decimal? MaxDistanceKm = MaximumAllowedRadiusKm,
        string? TransportMode = "Maritime"
    );
}
