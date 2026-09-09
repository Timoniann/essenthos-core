using System.Text.Json;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>One reading of one lexicon entry, with the model that read it and the day it was asked.</summary>
internal sealed record PlaceReadingRecord(
    bool? Place,
    int? Places,
    bool? Person,
    string? Why,
    string? Model,
    string? AskedAt);

/// <summary>
/// One entry of the lexicon the place pass considered, and what the four witnesses made of it.
/// </summary>
/// <param name="Names">
/// Every spelling the entry offers — Strong's transliteration, the head of his gloss, the
/// alternative he prints in brackets and the King James renderings he lists. It is what a held
/// place's label is met by, and it is computed by the harness because reading a name out of
/// nineteenth-century lexicon prose is the part of this that is not obvious.
/// </param>
/// <param name="Kept">
/// Whether the entry becomes a record. False lines are carried on purpose: what a register is asked
/// next is why something is not in it, and <paramref name="Why"/> answers that for the refusals as
/// well as for the records.
/// </param>
/// <param name="Witness">BHSA's own name type for the word, where BHSA has one. Hebrew only.</param>
/// <param name="StatedPlaces">
/// How many distinct places Strong states share the name, where he states it. Recorded and not yet
/// acted on: splitting one entry into several records is the namesake pass, not this one.
/// </param>
/// <param name="Person">Whether the same entry also names a person, which many of them do.</param>
internal sealed record PlaceRegisterRecord(
    string Number,
    string Name,
    IReadOnlyList<string>? Names,
    string? Transliteration,
    string? Definition,
    string? Morphology,
    bool Kept,
    string Why,
    string Tier,
    string? Witness,
    int? StatedPlaces,
    bool Person,
    PlaceReadingRecord? Reading,
    PlaceReadingRecord? Check);

/// <summary>
/// Where the place register is read from, and what one line of one has to be.
///
/// It is the output of the pass in <c>scripts/places.py</c> — one file per hundred entries, one JSON
/// object per line — and it lives with the corpus sources under this project's own folder, beside
/// the descriptors and the name forms, because it is a model run and not a fetch. A checkout
/// without it loads nothing and says so.
/// </summary>
internal static partial class PlaceRegisterFiles
{
    public const string ConfigurationKey = "Dataset:PlaceRegisterPath";

    public static readonly string DefaultFolder = Path.Combine("Essenthos", "places");

    public const string FilePattern = "*.jsonl";

    private static readonly JsonSerializerOptions Shape = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static IReadOnlyList<PlaceRegisterRecord> Read(string directory)
    {
        var records = new List<PlaceRegisterRecord>();
        foreach (var file in Directory
                     .EnumerateFiles(directory, FilePattern, SearchOption.AllDirectories)
                     .Order(StringComparer.Ordinal))
        {
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0)
                {
                    continue;
                }

                records.Add(
                    JsonSerializer.Deserialize<PlaceRegisterRecord>(line, Shape)
                    ?? throw new InvalidDataException(
                        $"A line of {file} is not a register entry. Each line must be one JSON "
                        + "object with at least a number, a name and a kept flag; regenerate the "
                        + "folder with \"python scripts/places.py publish\"."));
            }
        }

        return records;
    }

    /// <summary>The letters Strong prints for two, which no decomposition splits.</summary>
    private static readonly (string Printed, string Plain)[] Ligatures =
        [("æ", "ae"), ("œ", "oe"), ("ß", "ss")];

    /// <summary>
    /// Every Latin letter that decomposes to one ASCII letter, under the letter it is.
    ///
    /// Written out rather than decomposed, because this assembly is built with
    /// <c>InvariantGlobalization</c> and <c>String.Normalize</c> is a no-op under it — the trap
    /// <see cref="Utils.DiacriticFolding"/> already records. It cost 304 of the register's names
    /// before it was caught: <em>ʻIvvâh</em> came out <em>ivvh</em> and <em>Yᵉshanah</em> came out
    /// <em>yshanah</em>, neither met a held label, and each added a second page for a place the
    /// gazetteer already had.
    ///
    /// <para>
    /// The rows are the Latin blocks a transliteration can reach — the supplement, the two extended
    /// blocks, the spacing modifiers and the super- and subscripts — rather than the characters this
    /// corpus happens to hold today, because the failure they prevent is silent. Strong writes the
    /// shewa <em>ᵉ</em> 215 times and the emphatic <em>ṭ</em> 44, and neither of those is a letter
    /// anybody would think to list.
    /// </para>
    /// </summary>
    private static readonly (char Plain, string Accented)[] Latin =
    [
        ('a', "àáâãäåāăąǎǟǡǻȁȃȧᵃḁẚạảấầẩẫậắằẳẵặₐ"),
        ('b', "ᵇḃḅḇ"),
        ('c', "çćĉċčḉ"),
        ('d', "ďđᵈḋḍḏḑḓ"),
        ('e', "èéêëēĕėęěȅȇȩᵉḕḗḙḛḝẹẻẽếềểễệₑ"),
        ('f', "ḟ"),
        ('g', "ĝğġģǧǵᵍḡ"),
        ('h', "ĥħȟʰḣḥḧḩḫẖₕ"),
        ('i', "ìíîïĩīĭįıǐȉȋᵢḭḯỉịⁱ"),
        ('j', "ĵǰʲ"),
        ('k', "ķǩᵏḱḳḵₖ"),
        ('l', "ĺļľŀłˡḷḹḻḽₗ"),
        ('m', "ᵐḿṁṃₘ"),
        ('n', "ñńņňŉŋǹṅṇṉṋⁿₙ"),
        ('o', "òóôõöøōŏőơǒǫǭȍȏȫȭȯȱᵒṍṏṑṓọỏốồổỗộớờởỡợₒ"),
        ('p', "ᵖṕṗₚ"),
        ('r', "ŕŗřȑȓʳᵣṙṛṝṟ"),
        ('s', "śŝşšſșˢṡṣṥṧṩẛₛ"),
        ('t', "ţťŧțᵗṫṭṯṱẗₜ"),
        ('u', "ùúûüũūŭůűųưǔǖǘǚǜȕȗᵘᵤṳṵṷṹṻụủứừửữự"),
        ('v', "ᵛᵥṽṿ"),
        ('w', "ŵʷẁẃẅẇẉẘ"),
        ('x', "ˣẋẍₓ"),
        ('y', "ýÿŷȳʸẏẙỳỵỷỹ"),
        ('z', "źżžẑẓẕ"),
    ];

    private static readonly Dictionary<char, char> Plain = Latin
        .SelectMany(row => row.Accented.Select(accented => (accented, row.Plain)))
        .ToDictionary(pair => pair.accented, pair => pair.Plain);

    /// <summary>The combining marks a decomposed spelling carries, which are not letters.</summary>
    private const char MarksFrom = '̀';

    private const char MarksTo = 'ͯ';

    /// <summary>
    /// A name reduced to what two spellings of it share. OpenBible writes <em>Beth-lehem</em>,
    /// Strong writes <em>Bêyth Lechem</em> and the King James writes <em>Bethlehem</em>: the hyphen,
    /// the case, the accent and the ligature are spelling and not identity, so they come off before
    /// anything is compared.
    ///
    /// <para>
    /// It is strict about everything else. Nothing here folds one consonant onto another: that
    /// reaches a quarter more of the gazetteer and joins <em>Sion</em> to <em>Zoan</em> on the way,
    /// which is a wrong coordinate on a page rather than a missing one.
    /// </para>
    /// </summary>
    public static string Normalise(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return string.Empty;
        }

        var folded = name.ToLowerInvariant();
        foreach (var (printed, plain) in Ligatures)
        {
            folded = folded.Replace(printed, plain, StringComparison.Ordinal);
        }

        var kept = new System.Text.StringBuilder(folded.Length);
        foreach (var c in folded)
        {
            if (c is >= MarksFrom and <= MarksTo)
            {
                continue;
            }

            if (char.IsAsciiLetterOrDigit(c))
            {
                kept.Append(c);
            }
            else if (Plain.TryGetValue(c, out var letter))
            {
                kept.Append(letter);
            }
        }

        return kept.ToString();
    }

    /// <summary>
    /// A held label and the same label with the gazetteer's feature word taken off the front.
    ///
    /// OpenBible files the hill as <em>Mount Gilboa</em> and the wadi as <em>Valley of Eshcol</em>
    /// where the lexicon writes <em>Gilboa</em>. A hundred and twelve held places carry such a
    /// prefix, and indexing the bare name beside the full one is reading a naming convention rather
    /// than loosening the match — nothing here drops a vowel or folds a consonant.
    /// </summary>
    public static IEnumerable<string> Forms(string? label)
    {
        var name = (label ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            yield break;
        }

        yield return name;
        var bare = Feature().Replace(name, string.Empty).Trim();
        if (bare.Length > 0 && !string.Equals(bare, name, StringComparison.Ordinal))
        {
            yield return bare;
        }
    }

    [GeneratedRegex(
        @"^(?:mount|mt\.?|the|valley of|valley|wilderness of|wilderness|desert of|desert|river of"
        + @"|river|brook of|brook|sea of|sea|city of|city|land of|land|plain of|plain|plains of"
        + @"|hill of|hill|rock of|rock|gate of|gate|pool of|pool|well of|well|spring of|springs of"
        + @"|spring|springs|tower of|tower|cave of|cave|gulf of|lake|region of|district of|garden of"
        + @"|house of|field of|road to|way to|waters of|waters|upper|lower|gate)\s+",
        RegexOptions.IgnoreCase)]
    private static partial Regex Feature();
}
