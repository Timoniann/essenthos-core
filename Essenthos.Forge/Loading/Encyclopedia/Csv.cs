using System.Text;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// A CSV file as rows of named fields.
///
/// The project's <c>CsvParser</c> maps into typed objects by reflection, which needs a class per
/// file; there are fourteen files here and half their columns are read once. Reading them by name
/// keeps the shape of each file in the one place that uses it.
///
/// Quoting is the whole reason this is not <c>Split(',')</c>: the notes fields contain commas,
/// newlines and doubled quotes, and a naive split silently shifts every column after the first
/// such field.
/// </summary>
internal static class Csv
{
    public static IEnumerable<Dictionary<string, string>> Read(string path)
    {
        using var reader = new StreamReader(path, Encoding.UTF8);
        var header = Row(reader);
        if (header is null)
        {
            yield break;
        }

        while (Row(reader) is { } fields)
        {
            var row = new Dictionary<string, string>(header.Count, StringComparer.Ordinal);
            for (var i = 0; i < header.Count; i++)
            {
                row[header[i]] = i < fields.Count ? fields[i] : string.Empty;
            }

            yield return row;
        }
    }

    /// <summary>One record, which may span several lines when a field is quoted.</summary>
    private static List<string>? Row(StreamReader reader)
    {
        if (reader.EndOfStream)
        {
            return null;
        }

        var fields = new List<string>(20);
        var field = new StringBuilder(64);
        var quoted = false;

        while (true)
        {
            var next = reader.Read();
            if (next < 0)
            {
                break;
            }

            var character = (char)next;

            if (quoted)
            {
                if (character == '"')
                {
                    // A doubled quote inside a quoted field is one quote, not the end of it.
                    if (reader.Peek() == '"')
                    {
                        reader.Read();
                        field.Append('"');
                        continue;
                    }

                    quoted = false;
                    continue;
                }

                field.Append(character);
                continue;
            }

            switch (character)
            {
                case '"' when field.Length == 0:
                    quoted = true;
                    continue;
                case ',':
                    fields.Add(field.ToString());
                    field.Clear();
                    continue;
                case '\r':
                    continue;
                case '\n':
                    fields.Add(field.ToString());
                    return fields;
                default:
                    field.Append(character);
                    continue;
            }
        }

        fields.Add(field.ToString());
        return fields.Count == 1 && fields[0].Length == 0 ? null : fields;
    }
}
