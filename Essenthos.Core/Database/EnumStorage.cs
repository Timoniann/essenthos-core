using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Essenthos.Core.Database;

/// <summary>
/// How an enum is spelled in the database. The spellings are written out rather than derived from
/// the member names, so renaming a member cannot silently change what is already stored.
/// </summary>
internal static class EnumSpelling
{
    public static string Of(TextKind value) => value switch
    {
        TextKind.ManuscriptTradition => "manuscript-tradition",
        TextKind.CriticalEdition => "critical-edition",
        TextKind.Translation => "translation",
        _ => throw Unmapped(value),
    };

    public static TextKind ToTextKind(string stored) => stored switch
    {
        "manuscript-tradition" => TextKind.ManuscriptTradition,
        "critical-edition" => TextKind.CriticalEdition,
        "translation" => TextKind.Translation,
        _ => throw Unreadable<TextKind>(stored),
    };

    public static string Of(TextDirection value) => value switch
    {
        TextDirection.LeftToRight => "ltr",
        TextDirection.RightToLeft => "rtl",
        _ => throw Unmapped(value),
    };

    public static TextDirection ToTextDirection(string stored) => stored switch
    {
        "ltr" => TextDirection.LeftToRight,
        "rtl" => TextDirection.RightToLeft,
        _ => throw Unreadable<TextDirection>(stored),
    };

    public static string Of(Versification value) => value switch
    {
        Versification.Unknown => "unknown",
        Versification.Original => "original",
        Versification.English => "english",
        Versification.Septuagint => "septuagint",
        Versification.Vulgate => "vulgate",
        Versification.RussianOrthodox => "russian-orthodox",
        Versification.RussianProtestant => "russian-protestant",
        _ => throw Unmapped(value),
    };

    public static Versification ToVersification(string stored) => stored switch
    {
        "unknown" => Versification.Unknown,
        "original" => Versification.Original,
        "english" => Versification.English,
        "septuagint" => Versification.Septuagint,
        "vulgate" => Versification.Vulgate,
        "russian-orthodox" => Versification.RussianOrthodox,
        "russian-protestant" => Versification.RussianProtestant,
        _ => throw Unreadable<Versification>(stored),
    };

    public static string Of(Redistribution value) => value switch
    {
        Redistribution.Unknown => "unknown",
        Redistribution.PublicDomain => "public-domain",
        Redistribution.Permitted => "permitted",
        Redistribution.PermittedWithAttribution => "permitted-with-attribution",
        Redistribution.NonCommercialOnly => "non-commercial-only",
        Redistribution.Prohibited => "prohibited",
        _ => throw Unmapped(value),
    };

    public static Redistribution ToRedistribution(string stored) => stored switch
    {
        "unknown" => Redistribution.Unknown,
        "public-domain" => Redistribution.PublicDomain,
        "permitted" => Redistribution.Permitted,
        "permitted-with-attribution" => Redistribution.PermittedWithAttribution,
        "non-commercial-only" => Redistribution.NonCommercialOnly,
        "prohibited" => Redistribution.Prohibited,
        _ => throw Unreadable<Redistribution>(stored),
    };

    public static string Of(TextRelationKind value) => value switch
    {
        TextRelationKind.TranslatedFrom => "translated-from",
        TextRelationKind.RevisedFrom => "revised-from",
        TextRelationKind.SameFamilyAs => "same-family-as",
        TextRelationKind.CollatedAgainst => "collated-against",
        _ => throw Unmapped(value),
    };

    public static TextRelationKind ToTextRelationKind(string stored) => stored switch
    {
        "translated-from" => TextRelationKind.TranslatedFrom,
        "revised-from" => TextRelationKind.RevisedFrom,
        "same-family-as" => TextRelationKind.SameFamilyAs,
        "collated-against" => TextRelationKind.CollatedAgainst,
        _ => throw Unreadable<TextRelationKind>(stored),
    };

    public static string Of(LinkRelation value) => value switch
    {
        LinkRelation.Renders => "renders",
        LinkRelation.Equals => "equals",
        LinkRelation.Expands => "expands",
        LinkRelation.Omits => "omits",
        LinkRelation.Transposes => "transposes",
        _ => throw Unmapped(value),
    };

    public static LinkRelation ToLinkRelation(string stored) => stored switch
    {
        "renders" => LinkRelation.Renders,
        "equals" => LinkRelation.Equals,
        "expands" => LinkRelation.Expands,
        "omits" => LinkRelation.Omits,
        "transposes" => LinkRelation.Transposes,
        _ => throw Unreadable<LinkRelation>(stored),
    };

    public static string Of(LinkMethod value) => value switch
    {
        LinkMethod.StatedBySource => "stated-by-source",
        LinkMethod.StrongNumber => "strong-number",
        LinkMethod.Lexical => "lexical",
        LinkMethod.Aligner => "aligner",
        LinkMethod.Manual => "manual",
        _ => throw Unmapped(value),
    };

    public static LinkMethod ToLinkMethod(string stored) => stored switch
    {
        "stated-by-source" => LinkMethod.StatedBySource,
        "strong-number" => LinkMethod.StrongNumber,
        "lexical" => LinkMethod.Lexical,
        "aligner" => LinkMethod.Aligner,
        "manual" => LinkMethod.Manual,
        _ => throw Unreadable<LinkMethod>(stored),
    };

    public static string Of(LinkSide value) => value switch
    {
        LinkSide.From => "from",
        LinkSide.To => "to",
        _ => throw Unmapped(value),
    };

    public static LinkSide ToLinkSide(string stored) => stored switch
    {
        "from" => LinkSide.From,
        "to" => LinkSide.To,
        _ => throw Unreadable<LinkSide>(stored),
    };

    private static ArgumentOutOfRangeException Unmapped<TEnum>(TEnum value) where TEnum : struct =>
        new(nameof(value), value,
            $"{typeof(TEnum).Name}.{value} has no stored spelling. Add it to EnumSpelling in both " +
            "directions before using it.");

    private static InvalidOperationException Unreadable<TEnum>(string stored) =>
        new($"The database holds \"{stored}\" where a {typeof(TEnum).Name} was expected. Either something wrote a " +
            "value EnumSpelling does not know, or a member was renamed without its stored spelling being kept.");
}

/// <summary>
/// Enums are stored as the words DOC-0007 uses, not as ordinals. Every measurement this project
/// rests on was taken by hand in psql, and <c>select relation, count(*) from link group by 1</c>
/// answering <c>renders</c> rather than <c>2</c> is the difference between reading a result and
/// decoding one.
/// </summary>
internal static class EnumStorage
{
    public static readonly ValueConverter<TextKind, string> TextKind =
        new(value => EnumSpelling.Of(value), stored => EnumSpelling.ToTextKind(stored));

    public static readonly ValueConverter<TextDirection, string> TextDirection =
        new(value => EnumSpelling.Of(value), stored => EnumSpelling.ToTextDirection(stored));

    public static readonly ValueConverter<Versification, string> Versification =
        new(value => EnumSpelling.Of(value), stored => EnumSpelling.ToVersification(stored));

    public static readonly ValueConverter<Redistribution, string> Redistribution =
        new(value => EnumSpelling.Of(value), stored => EnumSpelling.ToRedistribution(stored));

    public static readonly ValueConverter<TextRelationKind, string> TextRelationKind =
        new(value => EnumSpelling.Of(value), stored => EnumSpelling.ToTextRelationKind(stored));

    public static readonly ValueConverter<LinkRelation, string> LinkRelation =
        new(value => EnumSpelling.Of(value), stored => EnumSpelling.ToLinkRelation(stored));

    public static readonly ValueConverter<LinkMethod, string> LinkMethod =
        new(value => EnumSpelling.Of(value), stored => EnumSpelling.ToLinkMethod(stored));

    public static readonly ValueConverter<LinkSide, string> LinkSide =
        new(value => EnumSpelling.Of(value), stored => EnumSpelling.ToLinkSide(stored));
}
