from pathlib import Path

path = Path('src/Dhole.AI.Api/Endpoints/AiLogisticsEndpoints.cs')
text = path.read_text(encoding='utf-8')

start = text.index('    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverPortsWithOverpassAsync(')
end = text.index('    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverNamedPortsWithNominatimAsync(', start)

overpass_method = '''    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverPortsWithOverpassAsync(
        HttpClient client,
        decimal latitude,
        decimal longitude,
        decimal radiusKm,
        CancellationToken cancellationToken
    )
    {
        var searchRadiiKm = new[]
        {
            Math.Min(radiusKm, 100m),
            Math.Min(radiusKm, 250m),
            radiusKm,
        }
        .Where(value => value >= 1m)
        .Distinct()
        .OrderBy(value => value)
        .ToArray();

        var ports = new List<DiscoveredPort>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var searchRadiusKm in searchRadiiKm)
        {
            try
            {
                var radiusMeters = Math.Clamp((int)Math.Ceiling(searchRadiusKm * 1000m), 1000, 500000);
                var lat = latitude.ToString(CultureInfo.InvariantCulture);
                var lon = longitude.ToString(CultureInfo.InvariantCulture);
                var query = $"[out:json][timeout:30];(nwr(around:{radiusMeters},{lat},{lon})[\\\"industrial\\\"=\\\"port\\\"][\\\"name\\\"];nwr(around:{radiusMeters},{lat},{lon})[\\\"port\\\"=\\\"cargo\\\"][\\\"name\\\"];nwr(around:{radiusMeters},{lat},{lon})[\\\"landuse\\\"=\\\"harbour\\\"][\\\"name\\\"];nwr(around:{radiusMeters},{lat},{lon})[\\\"seamark:type\\\"=\\\"harbour\\\"][\\\"name\\\"];nwr(around:{radiusMeters},{lat},{lon})[\\\"place\\\"=\\\"seaport\\\"][\\\"name\\\"];nwr(around:{radiusMeters},{lat},{lon})[\\\"seamark:harbour:category\\\"~\\\"cargo|container|bulk|tanker|roro\\\",i][\\\"name\\\"];nwr(around:{radiusMeters},{lat},{lon})[\\\"harbour:category\\\"~\\\"general|cargo|container|bulk|tanker|industrial|roro\\\",i][\\\"name\\\"];);out center tags;";

                using var body = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["data"] = query,
                });
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

                    var distanceKm = CalculateDistanceKm(latitude, longitude, portLatitude, portLongitude);
                    if (distanceKm > radiusKm) continue;

                    var key = $"{Normalize(name)}|{Math.Round(portLatitude, 3)}|{Math.Round(portLongitude, 3)}";
                    if (!seen.Add(key)) continue;

                    ports.Add(new DiscoveredPort(
                        name,
                        FirstNonEmpty(ReadString(tags, "locode"), ReadString(tags, "seamark:harbour:locode"), ReadString(tags, "ref")),
                        ReadString(tags, "addr:country"),
                        portLatitude,
                        portLongitude,
                        "OpenStreetMap/Overpass"
                    ));
                }

                if (ports.Count >= 20) break;
            }
            catch
            {
                // Public Overpass instances can throttle or time out. Continue with the next
                // radius and the geographic Nominatim fallback instead of returning false empty.
            }
        }

        return ports;
    }

'''
text = text[:start] + overpass_method + text[end:]

start = text.index('    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverNamedPortsWithNominatimAsync(')
end = text.index('    private static bool IsInternationalCargoPort(', start)

nominatim_method = '''    private static async Task<IReadOnlyCollection<DiscoveredPort>> DiscoverNamedPortsWithNominatimAsync(
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
        if (transportMode == "Land") return Array.Empty<DiscoveredPort>();

        var results = new List<DiscoveredPort>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var viewBox = BuildGeographicViewBox(pickupLatitude, pickupLongitude, radiusKm);

        var geographicQueries = transportMode == "Air"
            ? new[] { "international airport", "cargo airport", "airport" }
            : new[] { "cargo port", "container terminal", "commercial seaport", "seaport", "port" };

        foreach (var query in geographicQueries)
        {
            try
            {
                var url = $"search?format=jsonv2&limit=20&addressdetails=1&extratags=1&bounded=1&viewbox={Uri.EscapeDataString(viewBox)}&q={Uri.EscapeDataString(query)}";
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
                        || !TryParseCoordinate(row, "lon", out var longitude)) continue;

                    var distance = CalculateDistanceKm(pickupLatitude, pickupLongitude, latitude, longitude);
                    if (distance > radiusKm) continue;

                    var name = FirstNonEmpty(ReadString(row, "name"), ReadString(row, "display_name")?.Split(',').FirstOrDefault());
                    if (string.IsNullOrWhiteSpace(name)) continue;

                    var key = $"{Normalize(name)}|{Math.Round(latitude, 3)}|{Math.Round(longitude, 3)}";
                    if (!seen.Add(key)) continue;

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
                // Continue with the next geographic query.
            }
        }

        var addressLocality = pickupAddress?
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault();
        var localities = new[] { location.Locality, addressLocality, location.Region, location.Country }
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value!.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToArray();

        foreach (var locality in localities)
        {
            var queries = transportMode == "Air"
                ? new[] { $"{locality} International Airport", $"{locality} Airport" }
                : new[] { $"{locality} cargo port", $"{locality} container terminal", $"Port of {locality}", $"Puerto {locality}" };

            foreach (var query in queries)
            {
                try
                {
                    var url = $"search?format=jsonv2&limit=10&addressdetails=1&extratags=1&q={Uri.EscapeDataString(query)}";
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
                            || !TryParseCoordinate(row, "lon", out var longitude)) continue;

                        var distance = CalculateDistanceKm(pickupLatitude, pickupLongitude, latitude, longitude);
                        if (distance > radiusKm) continue;

                        var name = FirstNonEmpty(ReadString(row, "name"), ReadString(row, "display_name")?.Split(',').FirstOrDefault());
                        if (string.IsNullOrWhiteSpace(name)) continue;

                        var key = $"{Normalize(name)}|{Math.Round(latitude, 3)}|{Math.Round(longitude, 3)}";
                        if (!seen.Add(key)) continue;

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
                    // Continue with the next locality query.
                }
            }
        }

        return results;
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

        var south = Math.Max(-90d, lat - latDelta);
        var north = Math.Min(90d, lat + latDelta);
        var west = Math.Max(-180d, lon - lonDelta);
        var east = Math.Min(180d, lon + lonDelta);

        return string.Join(",",
            west.ToString("0.######", CultureInfo.InvariantCulture),
            north.ToString("0.######", CultureInfo.InvariantCulture),
            east.ToString("0.######", CultureInfo.InvariantCulture),
            south.ToString("0.######", CultureInfo.InvariantCulture));
    }

'''
text = text[:start] + nominatim_method + text[end:]

path.write_text(text, encoding='utf-8')
print('Nearest maritime port discovery patched successfully.')
