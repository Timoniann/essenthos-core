namespace Essenthos.Core.Endpoints;

/// <summary>
/// The <c>?verses=3-7</c> selector on the text endpoint. A single number is a range of one.
/// </summary>
internal static class VerseRange
{
    public static bool TryParse(string? value, out int from, out int to)
    {
        from = 0;
        to = 0;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var parts = value.Split('-', StringSplitOptions.TrimEntries);
        switch (parts.Length)
        {
            case 1 when int.TryParse(parts[0], out var single) && single > 0:
                from = single;
                to = single;
                return true;
            case 2 when int.TryParse(parts[0], out var start) && int.TryParse(parts[1], out var end)
                        && start > 0 && end >= start:
                from = start;
                to = end;
                return true;
            default:
                return false;
        }
    }

    public static string FormatHint(string? value)
    {
        return $"'{value}' is not a verse selector. Expected a verse number such as 3, or a range such " +
               "as 3-7 where the second number is not smaller than the first.";
    }
}
