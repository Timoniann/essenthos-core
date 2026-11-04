using System.Globalization;
using Essenthos.Core.Products;

namespace Essenthos.Core.Names;

/// <summary>
/// Where the names layer keeps its files under <c>Resources/Essenthos</c> (DOC-0224 §7): the settled names of the seven
/// reference texts, the rules and the bases the projection reads, each translation's overrides, and what the projection
/// writes.
/// </summary>
internal static class NameProducts
{
    public const string Folder = "names";

    /// <summary>The texts whose names are decided on the text itself: three originals and four witnesses.</summary>
    public static readonly IReadOnlyList<string> References = ["BHSA", "NESTLE1904", "TR1894", "GRCBRENT", "VULGCLEM", "SP", "GEEZ81"];

    public const string Rules = "names/rules";

    public const string Bases = "names/bases";

    public static string Settled(string reference) => $"names/{reference}";

    public static string Overrides(string text) => $"names/overrides/{text}";

    public static string Projected(string text) => $"names/projected/{text}";

    /// <summary>A database's names of a text as they stood when exported, by key: what the projection is proved against.</summary>
    public static string Live(string text) => $"names/live/{text}";

    public static bool IsReference(string text) => References.Contains(text, StringComparer.Ordinal);

    public static string? Number(double? value) => value?.ToString("R", CultureInfo.InvariantCulture);

    public static double? Number(string? text) => text is null ? null : double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}

/// <summary>
/// How a word stands for a record. One word may stand for several records, each in its own role: <em>Israel</em> for the
/// man and for the people, <em>Pharaoh</em> for the title and for the king who bears it.
/// </summary>
internal static class NameRoles
{
    /// <summary>The word is the record's name, or stands for the person or place where it is written.</summary>
    public const string Name = "name";

    /// <summary>The word names a people, a tribe or a nation.</summary>
    public const string People = "people";

    /// <summary>The word is a title, and the record the title or the one who bears it.</summary>
    public const string Title = "title";

    /// <summary>The word names an object, a feast, a term.</summary>
    public const string Thing = "thing";

    public static readonly IReadOnlySet<string> All = new HashSet<string>([Name, People, Title, Thing], StringComparer.Ordinal);

    /// <summary>The role a record's kind gives a word naming it, where nothing else says otherwise.</summary>
    public static string OfKind(string kind) => kind switch
    {
        "people" => People,
        "title" => Title,
        "object" or "observance" or "term" => Thing,
        _ => Name,
    };
}

/// <summary>
/// A name settled on a word of a reference text, <c>names/&lt;REF&gt;</c> (Hand TSV): the decision the projection carries
/// to every translation made from the text.
/// </summary>
/// <param name="Record">The record's slug.</param>
/// <param name="Role">How the word stands for it (<see cref="NameRoles"/>).</param>
/// <param name="Method">How it was decided: manual, stated-by-source, strong-number, rule-based, model-reading, lexical.</param>
/// <param name="Confidence">Null where a person or a source settled it.</param>
/// <param name="Source">Who says so.</param>
/// <param name="Note">What the decision rests on, or the text it was carried from before the names were frozen.</param>
internal sealed record SettledName(WordKey Own, string Record, string Role, string Method, double? Confidence, string Source, string? Note)
{
    public static ProductSchema<SettledName> Schema(string text) => new ProductSchema<SettledName>()
        .OwnWordKey("own", r => r.Own)
        .Text("record", r => r.Record)
        .Text("role", r => r.Role)
        .Text("method", r => r.Method)
        .OptionalText("confidence", r => NameProducts.Number(r.Confidence))
        .Text("source", r => r.Source)
        .OptionalText("note", r => r.Note)
        .Reads(row => new SettledName(
            WordKey.Parse(row.Text(0), text), row.Text(1), row.Text(2), row.Text(3), NameProducts.Number(row.TextOrNull(4)),
            row.Text(5), row.TextOrNull(6)));
}

/// <summary>What an override does to a word of a translation.</summary>
internal static class OverrideActions
{
    /// <summary>The word names the record though no word of the base it is linked to does.</summary>
    public const string Add = "add";

    /// <summary>The word does not name the record the projection gives it.</summary>
    public const string Suppress = "suppress";

    /// <summary>The translation names another record than its base does here: both are shown, the translation's first.</summary>
    public const string Differs = "differs";
}

/// <summary>
/// An exception on one word of one translation, <c>names/overrides/&lt;SLUG&gt;</c> (Hand TSV).
/// </summary>
/// <param name="Record">The record the word names in the translation (add, differs) or does not name (suppress).</param>
/// <param name="BaseRecord">For differs, the record the base names on the linked word.</param>
internal sealed record NameOverride(WordKey Own, string Record, string Action, string? BaseRecord, string Role, string Source, string? Note)
{
    public static ProductSchema<NameOverride> Schema(string text) => new ProductSchema<NameOverride>()
        .OwnWordKey("own", r => r.Own)
        .Text("record", r => r.Record)
        .Text("action", r => r.Action)
        .OptionalText("base_record", r => r.BaseRecord)
        .Text("role", r => r.Role)
        .Text("source", r => r.Source)
        .OptionalText("note", r => r.Note)
        .Reads(row => new NameOverride(
            WordKey.Parse(row.Text(0), text), row.Text(1), row.Text(2), row.TextOrNull(3), row.Text(4), row.Text(5), row.TextOrNull(6)));
}

/// <summary>
/// One line of the projection's rules, <c>names/rules</c> (Hand TSV): a word list entry or a threshold
/// (<see cref="NameRules"/> says what each rule means).
/// </summary>
/// <param name="Language">The ISO 639-3 code a word belongs to, or <c>*</c>.</param>
internal sealed record NameRuleRow(string Rule, string Language, string Value, string? Note)
{
    public static readonly ProductSchema<NameRuleRow> Schema = new ProductSchema<NameRuleRow>()
        .Text("rule", r => r.Rule)
        .Text("language", r => r.Language)
        .Text("value", r => r.Value)
        .OptionalText("note", r => r.Note)
        .Reads(row => new NameRuleRow(row.Text(0), row.Text(1), row.Text(2), row.TextOrNull(3)));
}

/// <summary>
/// The reference text a translation takes its names from in a range of books, <c>names/bases</c> (Hand TSV).
/// </summary>
/// <param name="Books">Canonical book ordinals, <c>1-39</c> or <c>67-93</c>.</param>
/// <param name="Why">The relation or the default that chose it.</param>
internal sealed record NameBase(string Text, string Books, string Base, string Why)
{
    public static readonly ProductSchema<NameBase> Schema = new ProductSchema<NameBase>()
        .Text("text", r => r.Text)
        .Text("books", r => r.Books)
        .Text("base", r => r.Base)
        .Text("why", r => r.Why)
        .Reads(row => new NameBase(row.Text(0), row.Text(1), row.Text(2), row.Text(3)));

    public (int First, int Last) Range
    {
        get
        {
            var dash = Books.IndexOf('-');
            return dash < 0
                ? (int.Parse(Books, CultureInfo.InvariantCulture), int.Parse(Books, CultureInfo.InvariantCulture))
                : (int.Parse(Books[..dash], CultureInfo.InvariantCulture), int.Parse(Books[(dash + 1)..], CultureInfo.InvariantCulture));
        }
    }
}

/// <summary>Where a name of the projection's output comes from.</summary>
internal static class NameOrigins
{
    /// <summary>A reference text's own settled name.</summary>
    public const string Settled = "settled";

    /// <summary>Carried from the base over a link of the final mapping.</summary>
    public const string Projected = "projected";

    /// <summary>An override's addition.</summary>
    public const string Added = "added";

    /// <summary>The translation's own record where it names another than its base (an override's differs).</summary>
    public const string Differs = "differs";

    /// <summary>The base's record on a word whose translation names another: shown second, never as the word's own name.</summary>
    public const string BaseReading = "base-reading";

    /// <summary>Whether a name of this origin is the word's own, as the database's word_entity holds it.</summary>
    public static bool IsOwn(string origin) => origin != BaseReading;
}

/// <summary>
/// A name on a word of a text as the projection gives it, <c>names/projected/&lt;SLUG&gt;</c> (Machine): settled on a
/// reference text, carried from the base over the final mapping, or set by an override.
/// </summary>
/// <param name="Origin">See <see cref="NameOrigins"/>.</param>
/// <param name="Through">The base's word it was carried from, with its text (<c>BHSA 1.1:1.3</c>); null when not carried.</param>
/// <param name="Confidence">The settled name's confidence times the link's, where either has one.</param>
/// <param name="Link">The mapping method of the link it was carried over (<see cref="Mapping.MappingMethods"/>); null when not carried.</param>
internal sealed record ProjectedName(
    WordKey Own,
    string Record,
    string Role,
    string Origin,
    string? Through,
    string Method,
    double? Confidence,
    string Source,
    string? Link = null)
{
    public static ProductSchema<ProjectedName> Schema(string text) => new ProductSchema<ProjectedName>()
        .OwnWordKey("own", r => r.Own)
        .Text("record", r => r.Record)
        .Text("role", r => r.Role)
        .Text("origin", r => r.Origin)
        .OptionalText("through", r => r.Through)
        .Text("method", r => r.Method)
        .OptionalText("confidence", r => NameProducts.Number(r.Confidence))
        .Text("source", r => r.Source)
        .OptionalText("link", r => r.Link)
        .Reads(row => new ProjectedName(
            WordKey.Parse(row.Text(0), text), row.Text(1), row.Text(2), row.Text(3), row.TextOrNull(4), row.Text(5),
            NameProducts.Number(row.TextOrNull(6)), row.Text(7), row.TextOrNull(8)));
}

/// <summary>
/// A name as a database holds it, <c>names/live/&lt;SLUG&gt;</c> (Machine), by the key of the word that carries the
/// database's word id in the text's words product: what the projection is compared with.
/// </summary>
/// <param name="Kind">The record's kind.</param>
/// <param name="Carried">The row was carried along a link from another text's word (its note begins <c>through</c>).</param>
internal sealed record LiveName(WordKey Own, string Record, string Kind, string Method, double? Confidence, string Source, string? Note, bool Carried)
{
    public static ProductSchema<LiveName> Schema(string text) => new ProductSchema<LiveName>()
        .OwnWordKey("own", r => r.Own)
        .Text("record", r => r.Record)
        .Text("kind", r => r.Kind)
        .Text("method", r => r.Method)
        .OptionalText("confidence", r => NameProducts.Number(r.Confidence))
        .Text("source", r => r.Source)
        .OptionalText("note", r => r.Note)
        .Boolean("carried", r => r.Carried)
        .Reads(row => new LiveName(
            WordKey.Parse(row.Text(0), text), row.Text(1), row.Text(2), row.Text(3), NameProducts.Number(row.TextOrNull(4)),
            row.Text(5), row.TextOrNull(6), row.Boolean(7)));
}
