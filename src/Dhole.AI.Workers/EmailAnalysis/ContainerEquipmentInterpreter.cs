namespace Dhole.AI.Worker.EmailAnalysis;

internal sealed record AiContainerDimensions(string Size, string Kind, string KindCode);

internal static class ContainerEquipmentInterpreter
{
    public static AiContainerDimensions? Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;

        var clean = new string(
            value.Trim().ToUpperInvariant().Where(char.IsLetterOrDigit).ToArray()
        );

        var size = clean.StartsWith("20", StringComparison.Ordinal) ? "20"
            : clean.StartsWith("40", StringComparison.Ordinal) ? "40"
            : clean.StartsWith("45", StringComparison.Ordinal) ? "45"
            : clean.StartsWith("48", StringComparison.Ordinal) ? "48"
            : null;

        if (size is null) return null;
        var suffix = clean[size.Length..];

        if (suffix.Contains("NONOPERATINGREEFER", StringComparison.Ordinal) || suffix.StartsWith("NOR", StringComparison.Ordinal))
            return new AiContainerDimensions(size, "NOR", "NOR");
        if (suffix.Contains("OPENTOP", StringComparison.Ordinal) || suffix.StartsWith("OT", StringComparison.Ordinal))
            return new AiContainerDimensions(size, "Open Top", "OT");
        if (suffix.Contains("OPENSIDE", StringComparison.Ordinal) || suffix.Contains("SIDEOPEN", StringComparison.Ordinal) || suffix.StartsWith("OS", StringComparison.Ordinal))
            return new AiContainerDimensions(size, "Open Side", "OS");
        if (suffix.Contains("FLATRACK", StringComparison.Ordinal) || suffix.StartsWith("FR", StringComparison.Ordinal))
            return new AiContainerDimensions(size, "Flat Rack", "FR");
        if (suffix.Contains("ISOTANK", StringComparison.Ordinal) || suffix.Contains("TANK", StringComparison.Ordinal) || suffix.StartsWith("TNK", StringComparison.Ordinal) || suffix.StartsWith("TK", StringComparison.Ordinal))
            return new AiContainerDimensions(size, "Tank", "TK");
        if (suffix.Contains("HIGHCUBE", StringComparison.Ordinal) || suffix.StartsWith("HC", StringComparison.Ordinal) || suffix.StartsWith("HQ", StringComparison.Ordinal))
            return new AiContainerDimensions(size, "High Cube", "HC");
        if (suffix.Length == 0 && size is "20" or "40"
            || suffix.StartsWith("DRY", StringComparison.Ordinal)
            || suffix.StartsWith("DV", StringComparison.Ordinal)
            || suffix.StartsWith("DC", StringComparison.Ordinal)
            || suffix.StartsWith("GP", StringComparison.Ordinal)
            || suffix.StartsWith("STD", StringComparison.Ordinal)
            || suffix.StartsWith("STANDARD", StringComparison.Ordinal))
            return new AiContainerDimensions(size, "Dry Van", "DV");

        return null;
    }
}
