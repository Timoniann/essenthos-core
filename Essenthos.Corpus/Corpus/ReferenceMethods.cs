namespace Essenthos.Core.Corpus;

/// <summary>
/// How this project came to list a verse under a record, said as one of a few kinds a reader can
/// tell apart.
///
/// Every reference of our own carries a sentence saying exactly how it was reached, and the
/// sentences are many and long — the model, the day, the ruling. Shown by whose they are, they all
/// read "Essenthos", so a page listing three ways of knowing names one source three times. The
/// kind is what the page names, in the reader's language; the sentence stays the record of it.
///
/// Declared by the way each sentence begins, as <see cref="Datasets"/> declares whose a row is: a
/// sentence nobody declared has no kind and is shown by whose it is, as before.
/// </summary>
public static class ReferenceMethods
{
    /// <summary>The words of a text this corpus marks as naming the record.</summary>
    public const string AnnotatedWords = "annotated-words";

    /// <summary>The Strong's dictionary entry of the word, and every word numbered with it.</summary>
    public const string Lexicon = "lexicon";

    /// <summary>The verse read by language models, which said whether it names or speaks of the record.</summary>
    public const string ModelReading = "model-reading";

    /// <summary>A ruling of the project's owner on a class of verses.</summary>
    public const string OwnerRuling = "owner-ruling";

    /// <summary>A review of records that were wrong or mixed, each verse read again.</summary>
    public const string Review = "review";

    /// <summary>The verse a family relationship of this project's own was read from.</summary>
    public const string Relationship = "relationship";

    private static readonly (string Prefix, string Method)[] Declared =
    [
        ("Essenthos, from the words this corpus annotates", AnnotatedWords),
        ("Essenthos, from Strong's Dictionary entry", Lexicon),
        ("Essenthos, read from the verse by", ModelReading),
        ("Essenthos, from the passages that command it, as a language model read them", ModelReading),
        ("Essenthos, names read again against their verses by", ModelReading),
        ("Essenthos, Hebrew names read against their verses by", ModelReading),
        ("Essenthos, the names the Hebrew marks that nothing read", ModelReading),
        ("Essenthos, the place names the Hebrew marks and the translations read as common words", ModelReading),
        ("Essenthos, on the project owner's ruling", OwnerRuling),
        ("Essenthos, on the project owner's decision", OwnerRuling),
        ("Essenthos, from the person split", Review),
        ("Essenthos, on the report of records holding several people", Review),
        ("Essenthos, on the portrait briefs' report", Review),
        ("Essenthos, on the review of the sense readings", Review),
        ("Essenthos, on Strong's entries numbering several men", Review),
        ("Essenthos, on the genealogy review's", Review),
        ("Essenthos, on the family relationships that contradicted themselves", Review),
        ("Essenthos, the family relationships left over", Review),
        ("Essenthos, two verses the tie-break", Review),
        ("Essenthos, the verse a relationship of this project's own was read from", Relationship),
        ("Essenthos, from the verse that calls the people's ancestor the father of it", Relationship),
    ];

    /// <summary>The kind of a reference's source sentence, or null where none is declared for it.</summary>
    public static string? Of(string? source)
    {
        if (source is null)
        {
            return null;
        }

        foreach (var (prefix, method) in Declared)
        {
            if (source.StartsWith(prefix, StringComparison.Ordinal))
            {
                return method;
            }
        }

        return null;
    }
}
