using System.Text.RegularExpressions;

namespace Dhole.AI.Worker.EmailAnalysis;

/// <summary>
/// Prevents non-pricing logistics evidence from being split into dozens of tariff
/// extraction stages. A conservative, filename-and-content classification avoids
/// blocking actual freight-rate sheets sharing logistics terminology.
/// </summary>
internal static class NonPricingDocumentGuard
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMilliseconds(100);
    private const RegexOptions Options =
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;

    internal static bool IsDocumentWithoutTariff(string? fileName, string? content)
    {
        var name = Path.GetFileNameWithoutExtension(fileName ?? "");
        var source = content ?? "";
        if (string.IsNullOrWhiteSpace(source))
            return false;

        var header = name + "\n" + source[..Math.Min(source.Length, 3500)];
        if (!Regex.IsMatch(header,
                @"\b(?:BILL OF LADING|HOUSE B\/L|MASTER B\/L|HBL|MBL|AIR WAYBILL|AWB|CARTA DE PORTE|COMMERCIAL INVOICE|FACTURA COMERCIAL|PACKING LIST|LISTA DE EMPAQUE|CARGO MANIFEST|MANIFIESTO DE CARGA)\b",
                Options, Timeout))
            return false;

        // Avoid a false negative for real tariffs: these words represent rate
        // offers, whereas declared customs values, freight prepaid/collect or
        // commercial invoice amounts are NOT external tariff offers.
        return !Regex.IsMatch(source,
            @"\b(?:TARIFF|TARIFARIO|TARIFAS?\s+(?:DE\s+)?FLETE|FREIGHT\s+RATES?|OCEAN\s+FREIGHT\s+RATES?|RATE\s+PER\s+(?:CBM|KG|CONTAINER|TEU)|COST\s+PER\s+(?:CBM|CONTAINER)|ALL[\s-]?IN\s+RATE|RATES?\s+VALID)\b",
            Options, Timeout);
    }
}
