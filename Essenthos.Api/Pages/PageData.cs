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
/// asks for as before.
/// </summary>
internal static class PageData
{
    /// <summary>The id of the script element that holds the answers, by which the reader's script finds them.</summary>
    public const string ElementId = "page-data";

    private static readonly JsonSerializerOptions Writing = new(AppJsonSerializerContext.Default.Options)
    {
        // Nothing in an answer can close the script element or start markup; the rest of Unicode is
        // written as itself, so Hebrew and Cyrillic cost a byte or two a letter rather than six.
        Encoder = JavaScriptEncoder.Create(UnicodeRanges.All),
    };

    /// <summary>The address a record's own answer is asked for at, in the page's language.</summary>
    public static string EntityAddress(string slug, SiteLanguage language) =>
        $"/v1/entities/{Uri.EscapeDataString(slug)}?language={language.Corpus}&prose={language.Corpus}";

    public static string Of<T>(string address, T answer) =>
        "{" + JsonSerializer.Serialize(address, Writing) + ":" +
        JsonSerializer.Serialize(answer, (JsonTypeInfo<T>)Writing.GetTypeInfo(typeof(T))) + "}";

    public static string Element(string data) =>
        $"<script type=\"application/json\" id=\"{ElementId}\">{data}</script>";
}
