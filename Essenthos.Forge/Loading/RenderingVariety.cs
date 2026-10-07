using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Links;

namespace Essenthos.Core.Loading;

/// <param name="Phrases">Every distinct phrase the text writes for the number, as printed.</param>
/// <param name="Renderings">
/// The distinct renderings once the words a language spends on grammar are set aside and the rest
/// reduced to their stems; null where the text's language has no list of those words here.
/// </param>
/// <param name="RenderingLinks">
/// The links whose phrase carries at least one word of its own: the denominator the renderings are
/// counted over. Links that write only a pronoun or a particle are outside it.
/// </param>
internal sealed record Variety(int Phrases, int? Renderings, int RenderingLinks);

/// <summary>One text's variety for every number; a number it writes no phrase for has none to count.</summary>
internal sealed class TextVariety(IReadOnlyDictionary<string, Variety> counted, Variety none)
{
    public Variety Of(string number) => counted.GetValueOrDefault(number) ?? none;
}

/// <summary>
/// How many different ways a text renders each Strong number, counted so that grammar a language
/// spends separately does not count as a different rendering.
///
/// <para>
/// A Hebrew word carries its pronoun, its article and its preposition inside itself, and the
/// English has to spend a separate word on each: אֱלֹהִים is <em>god</em>, <em>thy god</em>,
/// <em>our god</em>, <em>of god</em>, <em>the gods</em>. Counted as phrases that is dozens of
/// renderings of one choice, and the index would rank the commonest nouns by how many pronouns
/// they take. So each phrase is reduced to its words of content, each to its stem, and two phrases
/// reducing to the same are one rendering. A phrase with no word of content — <em>him</em> for the
/// object marker — is a rendering of grammar alone and is kept out of both the count and its
/// denominator.
/// </para>
///
/// <para>
/// Only for the languages listed here, each with a stemmer the corpus already uses. Any other
/// language is not counted at all rather than counted by spelling, which would rank its inflected
/// words first and say nothing about translation.
/// </para>
/// </summary>
internal static class RenderingVariety
{
    private static readonly char[] Marks =
        ['“', '”', '‘', '’', '"', '(', ')', '[', ']', '—', '–', ',', '.', ';', ':', '!', '?', '¶'];

    private static readonly IReadOnlySet<string> English = Words(
        "a an the and or nor but if then so that than as of to unto into in on upon at by for from with within without " +
        "out up off over under about against among through throughout before after behind beside between toward towards " +
        "i me my mine myself thou thee thy thine thyself he him his himself she her hers herself it its itself " +
        "we us our ours ourselves ye you your yours yourselves they them their theirs themselves " +
        "this these those which who whom whose what there here " +
        "be is am are was were been being shall shalt will wilt would should could might may must can " +
        "have hast hath has had do dost doth does did didst let o oh");

    private static readonly IReadOnlySet<string> Ukrainian = Words(
        "а аби або але без б би бо в від во до з за зі із й і та що щоб як чи же ж ну на над під по при про у через " +
        "я мене мені мною ти тебе тобі тобою він його йому ним нього ньому вона її їй нею неї ми нас нам нами " +
        "ви вас вам вами вони їх їм ними них це той та те ті цей ця ці " +
        "мій моя моє мої мого моєї моїх твій твоя твоє твої твого свій своя своє свої свого своїх " +
        "наш наша наше наші нашого ваш ваша ваше ваші вашого " +
        "бути є був була було були буде будуть будеш буду нехай хай");

    private static readonly IReadOnlySet<string> Russian = Words(
        "а без бы в во для до же за и из к ко как когда ли на над о об от по под при про с со у через что чтобы " +
        "я меня мне мною ты тебя тебе тобою он его ему им него нему она ее её ей ею неё мы нас нам нами " +
        "вы вас вам вами они их им ими них это этот эта эти тот та те " +
        "мой моя моё мое мои моего моей моих твой твоя твоё твое твои твоего свой своя своё свое свои своего своих " +
        "наш наша наше наши нашего ваш ваша ваше ваши вашего " +
        "быть есть был была было были будет будут будешь буду да пусть");

    private static readonly IReadOnlySet<string> German = Words(
        "der die das des dem den ein eine einen einem einer eines und oder aber denn doch daß dass so wie als wenn ob da " +
        "zu zum zur in im ins an am auf aus bei beim mit nach von vom vor über unter um durch für gegen bis " +
        "ich du er sie es wir ihr mich dich mir dir ihn ihm ihnen uns euch sich " +
        "mein meine meinen meinem meiner meines dein deine deinen deinem deiner deines sein seine seinen seinem seiner seines " +
        "ihre ihren ihrem ihrer ihres unser unsere unseren unserem unserer unseres euer eure euren eurem eurer eures " +
        "ist war sind waren wird werden ward wurde hat haben hatte soll sollst wollen will laß lass");

    private static readonly IReadOnlySet<string> Spanish = Words(
        "el la los las lo un una unos unas y e o u pero mas que de del a á al en con por para sin sobre entre hasta desde " +
        "él ella ellos ellas yo tú nosotros vosotros le les se me te nos os " +
        "su sus mi mis tu tus nuestro nuestra nuestros nuestras vuestro vuestra vuestros vuestras " +
        "es fué fue era son ha han había será serán");

    private static readonly IReadOnlySet<string> French = Words(
        "le la les l un une des du de d et ou mais que qu à au aux en dans par pour sur sous avec sans entre vers " +
        "je me m moi tu te t toi il elle lui on nous vous ils elles leur leurs se s eux " +
        "mon ma mes ton ta tes son sa ses notre nos votre vos ce cet cette ces " +
        "est était sont étaient sera seront a ont avait ne pas");

    private static readonly IReadOnlySet<string> Portuguese = Words(
        "o a os as um uma uns umas e ou mas que de do da dos das em no na nos nas por pelo pela pelos pelas para com sem " +
        "sobre entre até ao aos à às eu me mim tu te ti ele ela eles elas lhe lhes se nós vós " +
        "meu minha meus minhas teu tua teus tuas seu sua seus suas nosso nossa nossos nossas vosso vossa vossos vossas " +
        "é era são foi será serão há tem tinha");

    private static IReadOnlySet<string> Words(string words) =>
        new HashSet<string>(words.Split(' ', StringSplitOptions.RemoveEmptyEntries), StringComparer.Ordinal);

    /// <summary>The grammar words and the stemmer of a language, where it has both here.</summary>
    internal static (IReadOnlySet<string> Grammar, Func<string, string> Stem)? For(string language) => language switch
    {
        "eng" or "en" => (English, EnglishStemmer.Stem),
        "ukr" or "uk" => (Ukrainian, word => SlavicStemmer.Stem(word)),
        "rus" or "ru" => (Russian, word => SlavicStemmer.Stem(word)),
        "deu" or "de" => (German, GermanStemmer.Stem),
        "spa" or "es" => (Spanish, SpanishStemmer.Stem),
        "fra" or "fr" => (French, FrenchStemmer.Stem),
        "por" or "pt" => (Portuguese, PortugueseStemmer.Stem),
        _ => null,
    };

    /// <summary>
    /// The rendering a phrase is counted as: its words of content, stemmed, in the order the text
    /// prints them. Empty for a phrase of grammar alone.
    /// </summary>
    internal static string Key(string phrase, IReadOnlySet<string> grammar, Func<string, string> stem)
    {
        var content = phrase
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(word => word.Trim(Marks).ToLowerInvariant())
            .Where(word => word.Length > 0 && !grammar.Contains(word))
            .Select(stem)
            .Distinct(StringComparer.Ordinal);
        return string.Join(' ', content);
    }

    /// <summary>Each number's variety in one text, from every phrase the text writes for it.</summary>
    public static TextVariety Count(string language, IEnumerable<StrongRenderingCount> phrases)
    {
        var normaliser = For(language);
        var varieties = new Dictionary<string, Variety>(StringComparer.Ordinal);
        foreach (var number in phrases.GroupBy(phrase => phrase.Number, StringComparer.Ordinal))
        {
            if (normaliser is not { } normalise)
            {
                varieties[number.Key] = new Variety(number.Count(), null, 0);
                continue;
            }

            var keyed = number
                .Select(phrase => (Key: Key(phrase.Phrase, normalise.Grammar, normalise.Stem), phrase.Uses))
                .Where(phrase => phrase.Key.Length > 0)
                .ToList();
            varieties[number.Key] = new Variety(
                number.Count(),
                keyed.Select(phrase => phrase.Key).Distinct(StringComparer.Ordinal).Count(),
                keyed.Sum(phrase => phrase.Uses));
        }

        return new TextVariety(varieties, new Variety(0, normaliser is null ? null : 0, 0));
    }
}
