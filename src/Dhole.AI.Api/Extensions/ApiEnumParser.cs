namespace Dhole.AI.Api.Extensions;

public static class ApiEnumParser
{
    public static bool TryParse<TEnum>(string? value, out TEnum result)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = default;
            return false;
        }

        return Enum.TryParse(value.Trim(), ignoreCase: true, out result);
    }

    public static bool TryParseNullable<TEnum>(string? value, out TEnum? result)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = null;
            return true;
        }

        if (Enum.TryParse<TEnum>(value.Trim(), ignoreCase: true, out var parsed))
        {
            result = parsed;
            return true;
        }

        result = null;
        return false;
    }

    public static bool TryParseFlags<TEnum>(IReadOnlyCollection<string> values, out TEnum result)
        where TEnum : struct, Enum
    {
        long combined = 0;

        foreach (var value in values)
        {
            if (!Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed))
            {
                result = default;
                return false;
            }

            combined |= Convert.ToInt64(parsed);
        }

        result = (TEnum)Enum.ToObject(typeof(TEnum), combined);

        return true;
    }
}
