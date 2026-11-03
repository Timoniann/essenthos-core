using System.Text;
using System.Text.Json;

namespace Essenthos.Core.Conversion;

/// <summary>
/// A JSON value written the way Postgres prints a <c>jsonb</c>: keys ordered by length and then by their
/// bytes, the last of a repeated key kept, <c>", "</c> and <c>": "</c> between items, and only quotes,
/// backslashes and control characters escaped. A morphology written so is the string the corpus reads
/// back, which is what the verse's annotation hash is taken over.
/// </summary>
internal static class JsonbText
{
    public static string? Canonical(string? json)
    {
        if (json is null)
        {
            return null;
        }

        using var document = JsonDocument.Parse(json);
        var builder = new StringBuilder(json.Length + 16);
        Write(document.RootElement, builder);
        return builder.ToString();
    }

    private static void Write(JsonElement element, StringBuilder builder)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var properties = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    properties[property.Name] = property.Value;
                }

                builder.Append('{');
                var first = true;
                foreach (var (name, value) in properties.OrderBy(p => p.Key, KeyOrder.Instance))
                {
                    if (!first)
                    {
                        builder.Append(", ");
                    }

                    first = false;
                    WriteString(name, builder);
                    builder.Append(": ");
                    Write(value, builder);
                }

                builder.Append('}');
                break;
            case JsonValueKind.Array:
                builder.Append('[');
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    if (index++ > 0)
                    {
                        builder.Append(", ");
                    }

                    Write(item, builder);
                }

                builder.Append(']');
                break;
            case JsonValueKind.String:
                WriteString(element.GetString()!, builder);
                break;
            case JsonValueKind.Number:
                builder.Append(Number(element.GetRawText()));
                break;
            case JsonValueKind.True:
                builder.Append("true");
                break;
            case JsonValueKind.False:
                builder.Append("false");
                break;
            default:
                builder.Append("null");
                break;
        }
    }

    /// <summary>A number as <c>numeric</c> prints it: an exponent is written out; anything else is kept as given.</summary>
    private static string Number(string raw) =>
        raw.Contains('e', StringComparison.OrdinalIgnoreCase)
            ? decimal.Parse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture)
                .ToString(System.Globalization.CultureInfo.InvariantCulture)
            : raw;

    private static void WriteString(string value, StringBuilder builder)
    {
        builder.Append('"');
        foreach (var c in value)
        {
            switch (c)
            {
                case '"': builder.Append("\\\""); break;
                case '\\': builder.Append("\\\\"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < 0x20)
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        builder.Append('"');
    }

    private sealed class KeyOrder : IComparer<string>
    {
        public static readonly KeyOrder Instance = new();

        public int Compare(string? x, string? y)
        {
            var a = Encoding.UTF8.GetBytes(x!);
            var b = Encoding.UTF8.GetBytes(y!);
            return a.Length != b.Length ? a.Length.CompareTo(b.Length) : a.AsSpan().SequenceCompareTo(b);
        }
    }
}
