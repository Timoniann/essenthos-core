using System.Text;

namespace Essenthos.Core.Products.Tsv;

/// <summary>
/// The escaping of PostgreSQL's <c>COPY ... FROM STDIN</c> in text format, so that the body of a
/// product can be handed to <c>COPY</c> as it stands: a backslash, a tab, a line feed and a carriage
/// return are written as <c>\\</c>, <c>\t</c>, <c>\n</c> and <c>\r</c>, and null is <c>\N</c>.
/// Reading accepts what <c>COPY</c> accepts of the same kind (<c>\b \f \v</c> too, and any other
/// escaped character as itself); octal and hex escapes are not written and not read.
/// </summary>
public static class TsvEscaping
{
    public const string Null = @"\N";

    public static bool NeedsEscaping(ReadOnlySpan<char> value) => value.IndexOfAny("\\\t\n\r") >= 0;

    public static void WriteEscaped(TextWriter writer, string? value)
    {
        if (value is null)
        {
            writer.Write(Null);
            return;
        }

        if (!NeedsEscaping(value))
        {
            writer.Write(value);
            return;
        }

        foreach (var c in value)
        {
            switch (c)
            {
                case '\\': writer.Write(@"\\"); break;
                case '\t': writer.Write(@"\t"); break;
                case '\n': writer.Write(@"\n"); break;
                case '\r': writer.Write(@"\r"); break;
                default: writer.Write(c); break;
            }
        }
    }

    public static string Escape(string? value)
    {
        if (value is null)
        {
            return Null;
        }

        if (!NeedsEscaping(value))
        {
            return value;
        }

        var text = new StringWriter();
        WriteEscaped(text, value);
        return text.ToString();
    }

    /// <summary>One field as read between tabs; null for <c>\N</c>.</summary>
    public static string? Unescape(ReadOnlySpan<char> field)
    {
        var slash = field.IndexOf('\\');
        if (slash < 0)
        {
            return field.ToString();
        }

        if (field.Length == 2 && field[1] == 'N')
        {
            return null;
        }

        var text = new StringBuilder(field.Length);
        text.Append(field[..slash]);
        for (var i = slash; i < field.Length; i++)
        {
            var c = field[i];
            if (c != '\\' || i == field.Length - 1)
            {
                text.Append(c);
                continue;
            }

            i++;
            text.Append(field[i] switch
            {
                't' => '\t',
                'n' => '\n',
                'r' => '\r',
                'b' => '\b',
                'f' => '\f',
                'v' => '\v',
                var other => other,
            });
        }

        return text.ToString();
    }

    /// <summary>Splits one line into its unescaped fields. A line of one empty field is one field.</summary>
    public static string?[] SplitLine(ReadOnlySpan<char> line)
    {
        var count = 1;
        foreach (var c in line)
        {
            if (c == '\t')
            {
                count++;
            }
        }

        var fields = new string?[count];
        SplitLine(line, fields);
        return fields;
    }

    /// <summary>Splits into the given fields; the count of tabs must be one less than the fields.</summary>
    public static bool SplitLine(ReadOnlySpan<char> line, string?[] fields)
    {
        var index = 0;
        while (true)
        {
            var tab = line.IndexOf('\t');
            if (index == fields.Length)
            {
                return false;
            }

            if (tab < 0)
            {
                fields[index++] = Unescape(line);
                return index == fields.Length;
            }

            fields[index++] = Unescape(line[..tab]);
            line = line[(tab + 1)..];
        }
    }
}
