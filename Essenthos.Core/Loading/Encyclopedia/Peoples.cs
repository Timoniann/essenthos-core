using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// One tribe of Israel, named here because no lexicon names it.
///
/// Every other people in this layer comes out of a gentilic Strong derives: he writes <em>a Moabite
/// or descendant from Moab</em> and the record is his sentence with a page attached. The tribes are
/// the case that does not work, because Hebrew usually names a tribe with the ancestor's own word —
/// <c>maṭṭēh Yəhûdâ</c>, <c>bənê Yəhûdâ</c> — so the collective and the man are one lexeme and one
/// Strong number, and a dictionary of words has nothing to say about which of the two a verse
/// means. What witnesses it instead is BHSA, which marks that word <c>gens</c> where it stands for
/// the descent group; and what makes the record worth having is that the ancestor is already a page.
/// </summary>
/// <param name="Origin">The eponym's slug, which is the page a reader goes to for the man.</param>
/// <param name="CollectiveNumber">
/// The Strong number of the ancestor's own name — the word that does duty for the people. It is
/// never annotated wholesale: 800 words carry H3063 and BHSA marks nearly all of them
/// <c>pers,gens,topo</c> at once, which decides nothing. It is here so that an occurrence somebody
/// has individually read as the collective has a record to point at.
/// </param>
/// <param name="GentilicNumbers">
/// The Strong numbers that are this people's gentilic — <em>Reubenite</em> beside <em>Reuben</em>.
/// Where the gentilic parse read one of them, this claims it instead of a second record being made
/// from the same entry.
/// </param>
internal sealed record PeopleRecord(
    string Slug,
    string Name,
    string? Distinguisher,
    string Origin,
    string CollectiveNumber,
    IReadOnlyList<string>? GentilicNumbers,
    string Why,
    string? Notes);

/// <summary>
/// What to call the people of one gentilic entry, where neither of the two sources the naming rule
/// reads gives a usable word.
///
/// It is a correction and not a preference, so each carries the reason: Strong's own English for
/// H5680 is <em>an Eberite</em>, which nobody has ever said, and his King James list for H2680 is
/// the phrase <em>half of the Manahethites</em>, which is not a word the rule can take. Writing the
/// reason down is the difference between a correction and a silent rewrite of somebody else's
/// dictionary.
/// </summary>
internal sealed record PeopleNaming(string Number, string Name, string Why);

/// <summary>One occurrence a person or a review has decided names a people.</summary>
internal sealed record PeopleRuling(long WordId, string People, string Reference, string Why);

internal sealed record PeopleFile(
    string DecidedBy,
    string Policy,
    string Source,
    string Witness,
    IReadOnlyList<PeopleRecord> Tribes,
    IReadOnlyList<PeopleNaming> Namings,
    IReadOnlyList<PeopleRuling> Rulings);

/// <summary>
/// What to call the people a gentilic entry names, from the two things the entry itself says.
///
/// Strong's own English is a nineteenth-century transliteration — <em>a Chittite</em>, <em>a
/// Kenaanite</em>, <em>a Pelishtite</em> — and a page under those headwords is a page nobody finds.
/// Beside it he prints the King James renderings of the same word, which are the spellings every
/// reader knows, and those are taken first. Both are his; nothing here invents a name, and the nine
/// entries where neither field yields a word are corrected by hand with the reason recorded.
///
/// <para>
/// The plural is the form the record is titled in, because a people is many. English pluralises
/// these by adding <c>s</c> and there is no exception in the hundred and sixty — except the forms
/// that are already Hebrew plurals, <em>Anakim</em>, <em>Pathrusim</em>, <em>Caphthorim</em>, which
/// are left as they stand.
/// </para>
/// </summary>
internal static partial class GentilicNaming
{
    /// <summary>
    /// The endings that mark an English gentilic, in the order they are preferred. The plural forms
    /// come first so that a list offering both <em>Ziphim</em> and <em>Ziphite</em> yields the one
    /// a title wants, and the Hebrew plural comes last because it is a form and not a preference.
    /// </summary>
    private static readonly string[] Endings =
        ["ites", "ians", "eans", "ines", "ims", "ite", "ian", "ean", "ine", "im"];

    /// <summary>The Hebrew plural, which must not have an English one added to it.</summary>
    private const string HebrewPlural = "im";

    public static string? Of(string? kjvDefinition, string? definition) =>
        Plural(FromKingJames(kjvDefinition) ?? FromStrong(definition));

    /// <summary>
    /// The first single-word gentilic among the King James renderings Strong lists. Entries that
    /// are phrases — <em>children of Reuben</em>, <em>man of Tyre</em>, <em>of the house of
    /// Caleb</em> — are skipped rather than trimmed: what is left after trimming is a personal name
    /// or a common noun, and either would be a worse title than falling through to Strong's own
    /// word.
    /// </summary>
    private static string? FromKingJames(string? kjvDefinition)
    {
        if (string.IsNullOrWhiteSpace(kjvDefinition))
        {
            return null;
        }

        var words = Separators()
            .Split(Parenthetical().Replace(kjvDefinition, string.Empty))
            .Select(part => part.Trim().Trim('-').Trim())
            .Where(part => part.Length > 0 && Latin().IsMatch(part))
            .ToList();

        return Endings
            .Select(ending => words.FirstOrDefault(
                word => word.EndsWith(ending, StringComparison.OrdinalIgnoreCase)))
            .FirstOrDefault(found => found is not null);
    }

    /// <summary>
    /// The noun Strong's own definition opens with: <em>a Moabite or Moabitess</em> gives Moabite.
    /// Used where the King James list is a phrase or a Hebrew form, which is fourteen of the entries.
    /// </summary>
    private static string? FromStrong(string? definition) =>
        string.IsNullOrWhiteSpace(definition)
            ? null
            : Leading().Match(definition.Trim()) is { Success: true } found
                ? found.Groups[1].Value
                : null;

    private static string? Plural(string? name) =>
        name is null || name.EndsWith('s') || name.EndsWith(HebrewPlural, StringComparison.Ordinal)
            ? name
            : name + 's';

    [GeneratedRegex(@"\(.*?\)")]
    private static partial Regex Parenthetical();

    [GeneratedRegex(@"[,.;]")]
    private static partial Regex Separators();

    [GeneratedRegex(@"^[A-Za-z-]+$")]
    private static partial Regex Latin();

    [GeneratedRegex(@"^(?:an?|the)\s+([A-Za-z][A-Za-z-]*)")]
    private static partial Regex Leading();
}

/// <summary>
/// Whether a sentence describing a referent is describing a people.
///
/// The readings answered <c>unlisted</c> 1,096 times and wrote what they meant in free text, and
/// those sentences are the only evidence for what those occurrences name. They divide: 235 of the
/// Judah ones say <em>tribe</em>, <em>people</em> or <em>children of</em>, and as many again say
/// <em>kingdom</em>, <em>territory</em> or <em>land</em>. The first are this layer's; the second are
/// a polity and a place, which the encyclopedia has no kind for either, and folding them in here is
/// exactly the failure of a page for the Judahites that absorbs every occurrence of the word.
///
/// <para>
/// So a sentence naming both — <em>the kingdom/tribe of Judah</em>, 83 of them — decides nothing and
/// is refused. This reads the model's own words and never the verse; it decides which of two gaps an
/// answer falls in, not who is named.
/// </para>
/// </summary>
internal static partial class CollectiveReadings
{
    public static bool NamesAPeople(string? describes) =>
        !string.IsNullOrWhiteSpace(describes)
        && People().IsMatch(describes)
        && !Somewhere().IsMatch(describes);

    [GeneratedRegex(
        @"\b(tribe|tribes|tribal|people|peoples|nation|nations|clan|clans|famil(y|ies)|children|sons|descendants|house|men|inhabitants)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex People();

    [GeneratedRegex(
        @"\b(kingdom|kingdoms|territory|territorial|land|lands|region|province|city|cities|town|mount|mountain|valley|district|realm)\b",
        RegexOptions.IgnoreCase)]
    private static partial Regex Somewhere();
}

internal static class PeopleFiles
{
    private const string Resource = "Essenthos.Core.Loading.Encyclopedia.Peoples.json";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static PeopleFile Read()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(Resource)
                           ?? throw new FileNotFoundException(
                               $"The embedded resource \"{Resource}\" is not in this assembly. It is "
                               + "added by the EmbeddedResource item in Essenthos.Core.csproj; if the "
                               + "file was moved or renamed, that item and this name have to move with it.",
                               Resource);

        return JsonSerializer.Deserialize<PeopleFile>(stream, Shape)
               ?? throw new InvalidDataException($"The embedded resource \"{Resource}\" is empty.");
    }

    /// <summary>The longest a generated slug may run, so a page's address stays typeable.</summary>
    private const int SlugRoom = 60;

    /// <summary>
    /// The address a people's page lives at, from its name. Two peoples the King James spells alike
    /// — the Sabeans of Seba and the Sabeans of Sheba — cannot share one, so the second takes the
    /// Strong number of its own gentilic, which is the only thing that tells them apart.
    /// </summary>
    public static string Slug(string name, string number, Func<string, bool> taken)
    {
        var slug = new StringBuilder(SlugRoom);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                slug.Append(char.ToLowerInvariant(c));
            }
            else if (slug.Length > 0 && slug[^1] != '-')
            {
                slug.Append('-');
            }

            if (slug.Length >= SlugRoom)
            {
                break;
            }
        }

        var plain = slug.ToString().Trim('-');
        return taken(plain) ? $"{plain}-{number.ToLowerInvariant()}" : plain;
    }
}
