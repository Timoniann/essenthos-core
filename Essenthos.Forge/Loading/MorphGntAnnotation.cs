using System.Text.Json;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.MorphGnt;

namespace Essenthos.Core.Loading;

/// <summary>
/// Puts MorphGNT's morphology beside Nestle 1904's, word by word, so that the corpus holds two
/// analyses of the Greek New Testament instead of one.
///
/// <para>
/// One analysis cannot be checked. Nestle's own <c>case</c> attribute carried a gender for 20,772
/// words and the corpus could not notice, because nothing else had an opinion about those words;
/// it took a reader to find it. A second stated morphology makes that a query. It also fills in
/// what the first leaves out — 9,537 words gain a number, 9,534 a gender, 303 a degree the first
/// has no field for, and 4,602 forms Nestle can only call middle-or-passive are told which.
/// </para>
///
/// <para>
/// **Nothing here overwrites anything.** The rows go into <c>word_parsing</c> beside the word's own
/// <c>morphology</c> and the two stand together. Where they disagree — 440 cases, 70 genders, 29
/// tenses — that disagreement is a fact about the state of Greek scholarship on those words and is
/// worth more standing than resolved by whichever source was read second.
/// </para>
/// </summary>
internal static class MorphGntAnnotation
{
    /// <summary>
    /// Named in every row, because both licences over this data require attribution and this
    /// project attributes a source whether or not it has to. The version rather than the commit: a
    /// row has to say which dataset spoke, and which commit of it was read is recorded once, in the
    /// licence file kept beside the data, rather than 136,404 times here.
    /// </summary>
    internal const string Source =
        "morphgnt/sblgnt 6.12, the parsing MorphGNT gives the SBLGNT word standing in the same place";

    /// <summary>
    /// Both editions print the same letters and no third thing stands between them. It is not
    /// certainty — nobody stated this pairing, and the SBLGNT differs from the Nestle line in more
    /// than 540 variation units — so it sits at the top of the lexical band rather than outside it.
    /// </summary>
    internal const double PrintedConfidence = 0.98;

    /// <summary>
    /// The editions spell it differently and the difference is one of the two this corpus has
    /// measured them to differ by. Lower, and deliberately visible: these are the rows a reader
    /// checking the join should look at first, and the note on each says which two spellings were
    /// joined.
    /// </summary>
    internal const double SpellingConfidence = 0.85;

    /// <summary>What the word's own text says about it, flattened to the keys it writes.</summary>
    internal static IReadOnlyDictionary<string, string> Stated(JsonDocument? morphology)
    {
        if (morphology is null)
        {
            return new Dictionary<string, string>(0);
        }

        var stated = new Dictionary<string, string>(12, StringComparer.Ordinal);
        foreach (var property in morphology.RootElement.EnumerateObject())
        {
            if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } value)
            {
                stated[property.Name] = value;
            }
        }

        return stated;
    }

    /// <summary>
    /// The features, in the keys <c>word.morphology</c> uses, plus MorphGNT's own part of speech
    /// under <c>pos</c>. That vocabulary is finer than Nestle's on one point worth keeping: Nestle
    /// says <c>det</c> and <c>pron</c> where this separates the article from the personal,
    /// demonstrative, relative and interrogative pronouns, so the code is kept as written rather
    /// than mapped into a vocabulary that cannot hold it.
    /// </summary>
    internal static string Morphology(MorphGntWord word)
    {
        var features = new Dictionary<string, string>(10, StringComparer.Ordinal)
        {
            ["pos"] = word.PartOfSpeech,
            ["parse"] = word.Parse,
        };

        Add(features, "case", MorphGntParsing.Case(word.Parse));
        Add(features, "number", MorphGntParsing.Number(word.Parse));
        Add(features, "gender", MorphGntParsing.Gender(word.Parse));
        Add(features, "tense", MorphGntParsing.Tense(word.Parse));
        Add(features, "voice", MorphGntParsing.Voice(word.Parse));
        Add(features, "mood", MorphGntParsing.Mood(word.Parse));
        Add(features, "person", MorphGntParsing.Person(word.Parse));
        Add(features, "degree", MorphGntParsing.Degree(word.Parse));

        return JsonSerializer.Serialize(features, MorphologyJson);
    }

    private static readonly JsonSerializerOptions MorphologyJson = new() { WriteIndented = false };

    private static void Add(Dictionary<string, string> features, string name, string? value)
    {
        if (value is not null)
        {
            features[name] = value;
        }
    }

    /// <summary>
    /// Both spellings, on every row that needed them to differ, so a reader checking the join can
    /// see what was joined to what without going back to either edition.
    /// </summary>
    internal static string Note(JoinWord witness, MorphGntWord parsing) =>
        $"Nestle writes {witness.Surface} where the SBLGNT writes {parsing.Word}";
}
