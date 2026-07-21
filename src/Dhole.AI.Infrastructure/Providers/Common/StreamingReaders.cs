using System.Runtime.CompilerServices;

namespace Dhole.AI.Infrastructure.Providers.Common;

internal static class StreamingReaders
{
    public static async IAsyncEnumerable<string> ReadSseDataAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);

            if (line is null)
            {
                yield break;
            }

            if (!line.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var data = line["data:".Length..].Trim();

            if (string.IsNullOrWhiteSpace(data) || data == "[DONE]")
            {
                continue;
            }

            yield return data;
        }
    }

    public static async IAsyncEnumerable<string> ReadNdjsonAsync(
        Stream stream,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var line = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            yield return line;
        }
    }
}
