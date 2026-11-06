namespace Dhole.AI.Worker.Workers;

/// <summary>
/// Target the actual received timestamp of October 2026 emails in the
/// Costa Rican operating calendar, irrespective of the AI queue creation date.
/// This policy intentionally does not match November or September messages.
/// </summary>
public static class OctoberEmailSourcePolicy
{
    private static readonly TimeZoneInfo CostaRica =
        TimeZoneInfo.FindSystemTimeZoneById("America/Costa_Rica");

    public static bool ShouldClose(DateTime? originalReceivedAtUtc)
    {
        if (!originalReceivedAtUtc.HasValue)
            return false;

        var timestamp = originalReceivedAtUtc.Value;
        if (timestamp.Kind != DateTimeKind.Utc)
            timestamp = DateTime.SpecifyKind(timestamp, DateTimeKind.Utc);
        var receivedAtCostaRica = TimeZoneInfo.ConvertTimeFromUtc(timestamp, CostaRica);
        return receivedAtCostaRica.Year == 2026 && receivedAtCostaRica.Month == 10;
    }
}
