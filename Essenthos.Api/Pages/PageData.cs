using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.Unicode;

namespace Essenthos.Core.Pages;

/// <summary>
/// The API's own answers that a page was written from, carried in the page so the reader's script
/// starts from them instead of asking again: a JSON object from each answer's address, as the
/// script spells it (<c>/v1/entities/moses?language=eng&amp;prose=eng</c>), to the answer itself.
/// The script takes an answer only where it would ask for exactly that address; anything else it
/// asks for as before. Only a page's own answers are carried, never the ones every page shares,
/// which the browser keeps from one page to the next.
/// </summary>
internal static class PageData
{
    /// <summary>The id of the script element that holds the answers, by which the reader's script finds them.</summary>
    public const string ElementId = "page-data";

    /// <summary>How many of a Strong number's renderings the reader's page asks for at first.</summary>
    public const int StrongRenderings = 40;

    /// <summary>How many of an occupation's people the reader's page asks for at a time.</summary>
    public const int OccupationPeople = 40;

    private static readonly JsonSerializerOptions Writing = new(AppJsonSerializerContext.Default.Options)
    {
        // Nothing in an answer can close the script element or start markup; the rest of Unicode is
        // written as itself, so Hebrew and Cyrillic cost a byte or two a letter rather than six.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    public static string EntityAddress(string slug, SiteLanguage language) =>
        $"/v1/entities/{Uri.EscapeDataString(slug)}?language={language.Corpus}&prose={language.Corpus}";

    public static string EventAddress(string slug, SiteLanguage language) =>
        $"/v1/events/{Uri.EscapeDataString(slug)}?language={language.Corpus}&prose={language.Corpus}";

    public static string PeriodAddress(string slug) => $"/v1/periods/{Uri.EscapeDataString(slug)}";

    public static string StrongAddress(string number, SiteLanguage language) =>
        $"/v1/strong/{Uri.EscapeDataString(number)}?language={language.Corpus}";

    public static string StrongRenderingsAddress(string number, string corpus) =>
        $"/v1/strong/{Uri.EscapeDataString(number)}/renderings?corpus={Uri.EscapeDataString(corpus)}";

    public static string StrongBooksAddress(string number, string corpus) =>
        $"/v1/strong/{Uri.EscapeDataString(number)}/books?corpus={Uri.EscapeDataString(corpus)}";

    public static string PaneAddress(string text, string book, int chapter) =>
        $"/v1/panes/{Uri.EscapeDataString(text)}/{Uri.EscapeDataString(book)}/{chapter}";

    public static string OccupationAddress(string slug, SiteLanguage language) =>
        $"/v1/occupations/{Uri.EscapeDataString(slug)}?language={language.Corpus}&prose={language.Corpus}" +
        $"&skip=0&take={OccupationPeople}";

    /// <summary>An answer as the API writes it.</summary>
    public static string Json<T>(T answer) =>
        JsonSerializer.Serialize(answer, (JsonTypeInfo<T>)Writing.GetTypeInfo(typeof(T)));

    /// <summary>
    /// An answer the API keeps already written, such as a pane. JSON holds a <c>&lt;</c> only inside a
    /// string, where its escape means the same, so none can close the element.
    /// </summary>
    public static string Written(ReadOnlySpan<byte> utf8) => Encoding.UTF8.GetString(utf8).Replace("<", "\\u003c");

    /// <summary>The answers as one object, as UTF-8; null where there are none.</summary>
    public static byte[]? Of(IEnumerable<(string Address, string Json)> answers)
    {
        var data = new StringBuilder("{");
        foreach (var (address, json) in answers)
        {
            data.Append(data.Length > 1 ? "," : "").Append(JsonSerializer.Serialize(address, Writing)).Append(':').Append(json);
        }

        return data.Length > 1 ? Encoding.UTF8.GetBytes(data.Append('}').ToString()) : null;
    }

    public static string Element(byte[] data) =>
        $"<script type=\"application/json\" id=\"{ElementId}\">{Encoding.UTF8.GetString(data)}</script>";
}
