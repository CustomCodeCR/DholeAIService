using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Dhole.AI.Worker.EmailAnalysis;

/// <summary>
/// Reads AI-produced pricing rows defensively. Local models occasionally return
/// monetary values as formatted strings, arithmetic expressions, or numbers that
/// overflow <see cref="decimal"/>. One malformed optional amount must not discard
/// an otherwise usable pricing row.
/// </summary>
internal sealed partial class AiPricingEmailResultRowJsonConverter
    : JsonConverter<AiPricingEmailResultRow>
{
    private const decimal MaximumMoneyAbsoluteValue = 1_000_000_000m;
    private const decimal MaximumMarginAbsoluteValue = 100_000m;

    public override AiPricingEmailResultRow Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options
    )
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new JsonException("Cada elemento de rows debe ser un objeto JSON.");
        }

        return new AiPricingEmailResultRow(
            ReadString(root, "pol"),
            ReadString(root, "poe"),
            ReadString(root, "pod"),
            ReadString(root, "containerType"),
            ReadString(root, "carrier"),
            ReadString(root, "agent"),
            ReadString(root, "commodity"),
            ReadString(root, "currency"),
            ReadInt32(root, "freeDays"),
            ReadInt32(root, "transitDays"),
            ReadDateTime(root, "validFrom"),
            ReadDateTime(root, "validTo"),
            ReadDecimal(root, "oceanFreight"),
            ReadDecimal(root, "originCharges"),
            ReadDecimal(root, "destinationCharges"),
            ReadDecimal(root, "surcharges"),
            ReadDecimal(root, "totalCost"),
            ReadDecimal(root, "totalSale"),
            ReadDecimal(root, "profit"),
            ReadDecimal(root, "margin", MaximumMarginAbsoluteValue),
            ReadString(root, "spaceComment"),
            ReadString(root, "remarks")
        );
    }

    public override void Write(
        Utf8JsonWriter writer,
        AiPricingEmailResultRow value,
        JsonSerializerOptions options
    )
    {
        writer.WriteStartObject();
        WriteString(writer, "pol", value.Pol);
        WriteString(writer, "poe", value.Poe);
        WriteString(writer, "pod", value.Pod);
        WriteString(writer, "containerType", value.ContainerType);
        WriteString(writer, "carrier", value.Carrier);
        WriteString(writer, "agent", value.Agent);
        WriteString(writer, "commodity", value.Commodity);
        WriteString(writer, "currency", value.Currency);
        WriteNumber(writer, "freeDays", value.FreeDays);
        WriteNumber(writer, "transitDays", value.TransitDays);
        WriteDate(writer, "validFrom", value.ValidFrom);
        WriteDate(writer, "validTo", value.ValidTo);
        WriteNumber(writer, "oceanFreight", value.OceanFreight);
        WriteNumber(writer, "originCharges", value.OriginCharges);
        WriteNumber(writer, "destinationCharges", value.DestinationCharges);
        WriteNumber(writer, "surcharges", value.Surcharges);
        WriteNumber(writer, "totalCost", value.TotalCost);
        WriteNumber(writer, "totalSale", value.TotalSale);
        WriteNumber(writer, "profit", value.Profit);
        WriteNumber(writer, "margin", value.Margin);
        WriteString(writer, "spaceComment", value.SpaceComment);
        WriteString(writer, "remarks", value.Remarks);
        writer.WriteEndObject();
    }

    private static string? ReadString(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var value = element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText();
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static int? ReadInt32(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number
            && element.TryGetInt32(out var numericValue))
        {
            return numericValue is >= 0 and <= 100_000 ? numericValue : null;
        }

        var raw = element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        var match = IntegerRegex().Match(raw);
        return match.Success
            && int.TryParse(match.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            && value is >= 0 and <= 100_000
                ? value
                : null;
    }

    private static DateTime? ReadDateTime(JsonElement root, string propertyName)
    {
        if (!TryGetProperty(root, propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var raw = element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        return DateTime.TryParse(
            raw,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeUniversal,
            out var value
        )
            ? value
            : null;
    }

    private static decimal? ReadDecimal(
        JsonElement root,
        string propertyName,
        decimal maximumAbsoluteValue = MaximumMoneyAbsoluteValue
    )
    {
        if (!TryGetProperty(root, propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        var raw = element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : element.GetRawText();
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        if (TryParseDecimal(raw, maximumAbsoluteValue, out var directValue))
        {
            return directValue;
        }

        // Do not try to salvage overflowing JSON numeric tokens. This fallback is
        // intended for model strings such as "$15/cntr + $50/cntr" or "USD 65".
        if (element.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        decimal total = 0m;
        var foundValue = false;
        foreach (Match match in MonetaryAmountRegex().Matches(raw))
        {
            var token = match.Groups["amount"].Value.Replace(",", string.Empty);
            if (!TryParseDecimal(token, maximumAbsoluteValue, out var amount))
            {
                continue;
            }

            try
            {
                total = checked(total + amount);
                foundValue = true;
            }
            catch (OverflowException)
            {
                return null;
            }
        }

        return foundValue
            && total <= maximumAbsoluteValue
            && total >= -maximumAbsoluteValue
                ? total
                : null;
    }

    private static bool TryParseDecimal(
        string raw,
        decimal maximumAbsoluteValue,
        out decimal value
    )
    {
        var normalized = raw
            .Trim()
            .Replace("USD", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("EUR", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("CRC", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("US$", string.Empty, StringComparison.OrdinalIgnoreCase)
            .Replace("$", string.Empty, StringComparison.Ordinal)
            .Trim();

        if (!decimal.TryParse(
                normalized,
                NumberStyles.Float | NumberStyles.AllowThousands,
                CultureInfo.InvariantCulture,
                out value
            )
            || value > maximumAbsoluteValue
            || value < -maximumAbsoluteValue)
        {
            value = default;
            return false;
        }

        return true;
    }

    private static bool TryGetProperty(
        JsonElement root,
        string propertyName,
        out JsonElement value
    )
    {
        if (root.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        foreach (var property in root.EnumerateObject())
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

    private static void WriteString(Utf8JsonWriter writer, string name, string? value)
    {
        if (value is null)
        {
            writer.WriteNull(name);
        }
        else
        {
            writer.WriteString(name, value);
        }
    }

    private static void WriteDate(Utf8JsonWriter writer, string name, DateTime? value)
    {
        if (value.HasValue)
        {
            writer.WriteString(name, value.Value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, int? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(name, value.Value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    private static void WriteNumber(Utf8JsonWriter writer, string name, decimal? value)
    {
        if (value.HasValue)
        {
            writer.WriteNumber(name, value.Value);
        }
        else
        {
            writer.WriteNull(name);
        }
    }

    [GeneratedRegex(@"[-+]?\d+", RegexOptions.CultureInvariant)]
    private static partial Regex IntegerRegex();

    [GeneratedRegex(
        @"(?<![\p{L}\d])(?:USD|EUR|CRC|US\$|\$)?\s*(?<amount>[-+]?\d[\d,]*(?:\.\d+)?(?:[eE][-+]?\d+)?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    )]
    private static partial Regex MonetaryAmountRegex();
}
