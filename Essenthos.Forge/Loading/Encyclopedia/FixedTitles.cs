using System.Text.RegularExpressions;
using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>An original word with what the rules read off it and off the words around it.</summary>
/// <param name="Previous">The word before it in the verse, or null at the start of the verse.</param>
/// <param name="Following">The Strong numbers of the words after it in the verse, nearest first.</param>
/// <param name="Nth">Which occurrence of its number in the verse it is.</param>
internal sealed record FixedTitleWord(
    long Id,
    string Text,
    int Book,
    string Number,
    FixedTitleMorphology Morphology,
    FixedTitleMorphology? Previous,
    IReadOnlyList<string> Following,
    int Chapter = 0,
    int Verse = 0,
    int Nth = 1);

/// <param name="Pos">BHSA's or Nestle's part of speech: <c>art</c> is BHSA's article, <c>det</c> Nestle's.</param>
/// <param name="Form">Nestle's parsing code, which carries the number where the morphology has no field for it.</param>
internal sealed record FixedTitleMorphology(string? Pos, string? Case, string? Number, string? State, string? Form);

/// <summary>
/// One title the text fixes to one bearer, and the shape a word has to have to be that title.
/// </summary>
/// <param name="Slug">The record the title names.</param>
/// <param name="Books">The canonical books the title holds in; null for every book of the text.</param>
/// <param name="Article">Whether the word must stand with its own article, agreeing with it in Greek.</param>
/// <param name="Construct">Whether the Hebrew word must be in the construct state.</param>
/// <param name="Then">The Strong numbers the words after it must carry, in order.</param>
/// <param name="Note">What the annotation says about the title, written on it and on its claim.</param>
/// <param name="Except">The verses where the word is the title and the text leaves open whose.</param>
internal sealed record FixedTitleRule(
    string Slug,
    string Text,
    string Number,
    IReadOnlySet<int>? Books,
    bool Article,
    bool Construct,
    IReadOnlyList<string> Then,
    string Note,
    IReadOnlyList<ScriptureSpan>? Except = null)
{
    public bool Holds(FixedTitleWord word) =>
        word.Text == Text
        && word.Number == Number
        && (Books is null || Books.Contains(word.Book))
        && (Except is null || !Except.Any(span => span.Holds(word.Book, word.Chapter, word.Verse, word.Nth)))
        && FixedTitles.Singular(word.Morphology)
        && (!Article || FixedTitles.HasArticle(word))
        && (!Construct || word.Morphology.State == "c")
        && word.Following.Take(Then.Count).SequenceEqual(Then);
}

/// <summary>
/// Titles the text itself fixes to one bearer, so that the word, wherever it has the shape the rule
/// asks for, names that bearer and nobody else. Each rule was measured against the dataset's verse
/// list before it was written here; the figures are on the rule.
/// </summary>
internal static partial class FixedTitles
{
    private const int Revelation = 66;

    private const int Ezekiel = 26;

    public static readonly IReadOnlyList<string> GreekWitnesses =
        [.. EntityCandidates.GreekWitnesses, TischendorfTextSource.Slug, WestcottHortTextSource.Slug];

    public static readonly IReadOnlyList<FixedTitleRule> Rules =
    [
        // ὁ διάβολος, singular with its own article: 29 words in 27 verses; the dataset files 24 of them
        // under Satan and leaves out Rev 12:12, Jas 4:7 and 1 John 3:10, which say the same. Without the
        // article the word is a description (John 6:70, Acts 13:10) and in the plural 'slanderers'.
        .. GreekWitnesses.Select(witness => new FixedTitleRule(
            "satan", witness, "G1228", null, Article: true, Construct: false, [],
            "ὁ διάβολος, singular with the article: the devil, whom Rev 12:9 and 20:2 call Satan")),
        // The two verses that say so name him Διάβολος without an article and 'the Satan' beside it.
        .. GreekWitnesses.Select(witness => new FixedTitleRule("satan", witness, "G1228", null, Article: false, Construct: false,
            ["G2532", "G3588", "G4567"],
            "Διάβολος beside 'and the Satan': the verse gives both names to one bearer")),
        // Some witnesses give the same two names without a second article.
        .. GreekWitnesses.Select(witness => new FixedTitleRule("satan", witness, "G1228", null, Article: false, Construct: false,
            ["G2532", "G4567"],
            "Διάβολος beside 'and Satan': the verse gives both names to one bearer")),
        // In Revelation the dragon and the serpent are the one Rev 12:9 and 20:2 call the devil and
        // Satan when the title stands with its article. The dragon
        // of 12:3 is introduced without one, and 13:11's 'as a dragon' is a likeness.
        .. GreekWitnesses.Select(witness => new FixedTitleRule("satan", witness, "G1404", new HashSet<int> { Revelation }, Article: true,
            Construct: false, [], "ὁ δράκων in Revelation, whom 12:9 and 20:2 call the devil and Satan")),
        .. GreekWitnesses.Select(witness => new FixedTitleRule("satan", witness, "G3789", new HashSet<int> { Revelation }, Article: true,
            Construct: false, [], "ὁ ὄφις in Revelation, whom 12:9 and 20:2 call the devil and Satan")),
        // הַשָּׂטָן with the article: Job 1-2 and Zech 3:1-2, 17 words, every verse filed under Satan.
        // Without it the word is 'an adversary' (Num 22:22, 1 Kgs 11:14) and is left to the readings.
        new("satan", BhsaTextSource.Slug, "H7854", null, Article: true, Construct: false, [],
            "הַשָּׂטָן with the article: the text uses a title here, 'the adversary', and not yet a name"),
        // Χριστός, 529 words in 499 verses, 492 of them filed under Jesus; the 7 the dataset leaves out
        // (Rom 15:5, Gal 2:19, Phil 1:16, 4:7, 1 Thess 2:6, Jas 2:1, 1 Pet 1:7) mean him as plainly, four
        // of them beside his name. The dataset files the Gospels' questions about the Christ under Jesus
        // too, 54 of 54; those are not his by the verse, which asks, denies or reports a claim, and the
        // rulings on the title leave them to the title alone. Read in each Greek witness, because the
        // Byzantine and the Received Text print Χριστός in some forty verses where Nestle does not, and
        // Robinson and Stephanus are not linked to Nestle at all.
        .. GreekWitnesses.Select(witness => new FixedTitleRule(
            "jesus", witness, "G5547", null, Article: false, Construct: false, [],
            "Χριστός, the title the New Testament gives Jesus",
            Except: SenseReadingFiles.TitleReadings().Open("G5547"))),
        // ὁ υἱὸς τοῦ ἀνθρώπου: 81 words in 77 verses, every one filed under Jesus. Without the articles it
        // is Daniel's 'a son of man' (John 5:27, Heb 2:6, Rev 1:13, 14:14), and is left to the readings.
        .. GreekWitnesses.Select(witness => new FixedTitleRule("jesus", witness, "G5207", null, Article: false, Construct: false, ["G3588", "G444"],
            "ὁ υἱὸς τοῦ ἀνθρώπου, the Son of Man, Jesus's name for himself")),
        // בֶּן־אָדָם in Ezekiel is how God addresses the prophet, 93 times from 2:1; the dataset files 83
        // of the verses under him and leaves out ten that address him the same way (8:8, 17:2, 38:14).
        // Daniel 8:17 addresses Daniel so, which is why the rule stops at the book.
        new("ezekiel", BhsaTextSource.Slug, "H1121", new HashSet<int> { Ezekiel }, Article: false,
            Construct: true, ["H120"], "בֶּן־אָדָם, 'son of man', as God addresses Ezekiel throughout his book"),
    ];

    public static IEnumerable<string> Numbers => Rules.Select(rule => rule.Number).Distinct();

    /// <summary>The first rule the word holds for, if any.</summary>
    public static FixedTitleRule? Of(FixedTitleWord word) => Rules.FirstOrDefault(rule => rule.Holds(word));

    public static bool Singular(FixedTitleMorphology morphology) =>
        morphology.Number is "sg" or "singular"
        || (morphology.Number is null && morphology.Form is { } form && SingularForm().IsMatch(form));

    /// <summary>
    /// BHSA writes the article as a word of its own before the noun. The Greek article must agree
    /// with the noun; read the shared parsing code before the expanded fields.
    /// </summary>
    public static bool HasArticle(FixedTitleWord word)
    {
        if (word.Previous is not { } previous)
            return false;

        if (word.Text == BhsaTextSource.Slug)
            return previous.Pos == "art";

        if (previous.Form is not null && word.Morphology.Form is not null)
        {
            var article = GreekMorphology.Parse(previous.Form);
            return article.Part == GreekPart.Article && article.Number == 'S'
                && article.Agrees(GreekMorphology.Parse(word.Morphology.Form));
        }

        return previous.Pos == "det" && previous.Case is not null
            && previous.Case == word.Morphology.Case && Singular(previous);
    }

    [GeneratedRegex("^[A-Z0-9]+-[NGDAV]S")]
    private static partial Regex SingularForm();
}
