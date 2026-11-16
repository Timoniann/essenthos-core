using System.Globalization;
using Essenthos.Core.Conversion;
using Essenthos.Core.Products;

namespace Essenthos.Core.Mapping;

/// <summary>
/// Whether the links of different words of a translation to the same original word are one rendering of it, which stand
/// side by side, or two readings of the word of which one is wrong (<c>MappingMerge.Compete</c>). They are one rendering
/// when the words stand together in their verse; when nothing between them is given by a surer link to another original
/// word; or when the words of one link are all words the language writes apart from the word they belong to (a subject or
/// object pronoun, an auxiliary, the second half of a negation, a possessive, a separable particle closing its clause), as
/// in "je ne le ferai pas" or "ich will dir das Land geben". Across a gap, two links never are one rendering when a
/// sentence ends between them, when the words of either are all articles, or when a word of one is spelt as a word of the
/// other: that is the word's other occurrence.
/// </summary>
internal sealed class RenderingWords
{
    private static readonly Dictionary<string, string> WrittenApartWords = new(StringComparer.Ordinal)
    {
        ["fra"] = "ne n’ n' pas point plus jamais rien guère aucun aucune personne nul nulle "
                  + "je j’ j' tu il elle on nous vous ils elles se s’ s' me m’ m' te t’ t' lui leur leurs "
                  + "ai as a avons avez ont avais avait avions aviez avaient eus eut eûmes eûtes eurent aurai auras aura aurons aurez auront "
                  + "suis es est sommes êtes sont étais était étions étiez étaient fus fut fûmes fûtes furent serai seras sera serons serez seront "
                  + "mon ma mes ton ta tes son sa ses notre nos votre vos",
        ["deu"] = "ich du er sie es wir ihr man wer sich mich dich uns euch mir dir ihm ihn ihnen nicht "
                  + "bin bist ist sind seid war warst waren wart sei seiest wäre wären werde wirst wird werden werdet wurde wurdest wurden ward "
                  + "habe hast hat haben habt hatte hattest hatten hätte hättest hätten will willst wollen wollt wollte wollten "
                  + "soll sollst sollen sollt sollte sollten kann kannst können könnt konnte konnten mag magst mögen möge "
                  + "muß muss mußt musst müssen müßt mußte darf darfst dürfen "
                  + "mein meine meinen meinem meiner meines dein deine deinen deinem deiner deines sein seine seinen seinem seiner seines "
                  + "ihre ihren ihrem ihrer ihres unser unsere unseren unserem unserer unseres unsern euer eure euren eurem eurer eures",
        ["eng"] = "i thou he she it we ye you they me thee him us them not "
                  + "shall shalt will wilt would wouldest should shouldest may mayest might mightest can canst could couldest must "
                  + "do dost doth does did didst have hast hath has had hadst am art is are was wast were wert be been "
                  + "my thy his her its our your their mine thine",
        ["spa"] = "yo tú él ella nosotros vosotros ellos ellas no nunca jamás nada nadie ni me te se nos os le les "
                  + "he has ha hemos habéis han había habías habíamos habíais habían hube hubo habrá habré habrás habremos habréis habrán "
                  + "mi mis tu tus su sus nuestro nuestra nuestros nuestras vuestro vuestra vuestros vuestras",
        ["por"] = "eu tu ele ela nós vós eles elas não nunca jamais nada ninguém nem me te se nos vos lhe lhes "
                  + "hei hás há havemos haveis hão havia tenho tens tem temos tendes têm tinha tinham "
                  + "meu minha meus minhas teu tua teus tuas seu sua seus suas nosso nossa nossos nossas vosso vossa vossos vossas",
        ["rus"] = "я ты он она оно мы вы они меня тебя нас вас мне тебе нам вам ему ей им не ни "
                  + "буду будешь будет будем будете будут был была было были его ее её их "
                  + "мой моя моё мое мои моего моей мою моих твой твоя твоё твое твои твоего твоей твою твоих "
                  + "наш наша наше наши нашего нашей нашу наших ваш ваша ваше ваши вашего вашей вашу ваших "
                  + "свой своя своё свое свои своего своей свою своих",
        ["ukr"] = "я ти він вона воно ми ви вони мене тебе нас вас мені тобі нам вам йому їй їм не ні "
                  + "буду будеш буде будемо будете будуть був була було були його її їх "
                  + "мій моя моє мої мого моєї мою моїх твій твоя твоє твої твого твоєї твою твоїх "
                  + "наш наша наше наші нашого нашої нашу наших ваш ваша ваше ваші вашого вашої вашу ваших "
                  + "свій своя своє свої свого своєї свою своїх",
    };

    /// <summary>
    /// Articles, which stand beside their noun: across a gap a link from one is never one rendering with another, whatever
    /// the other's words are.
    /// </summary>
    private static readonly Dictionary<string, string> BesideWords = new(StringComparer.Ordinal)
    {
        ["fra"] = "le la les l’ l' un une des du au aux",
        ["deu"] = "der die das den dem des ein eine einen einem einer eines",
        ["eng"] = "the a an",
        ["spa"] = "el la los las un una unos unas lo",
        ["por"] = "o a os as um uma uns umas",
    };

    /// <summary>
    /// Conjunctions, which join words rather than render one: a link whose words are all conjunctions or articles holds no
    /// original word against another link (<see cref="Slight"/>).
    /// </summary>
    private static readonly Dictionary<string, string> JoiningWords = new(StringComparer.Ordinal)
    {
        ["fra"] = "et ou mais car donc",
        ["deu"] = "und oder aber denn",
        ["eng"] = "and or but for",
        ["spa"] = "y e o u pero",
        ["por"] = "e ou mas",
        ["rus"] = "и а но или да",
        ["ukr"] = "і й та а але або",
    };

    /// <summary>Words that open a phrase another word closes, around what they govern: "um seines Knechtes willen".</summary>
    private static readonly Dictionary<string, string> Circumpositions = new(StringComparer.Ordinal)
    {
        ["deu"] = "um/willen von/wegen um/halben",
        ["eng"] = "for/sake for/sakes",
    };

    /// <summary>The particles a separable verb leaves at the end of its clause, a word apart only there.</summary>
    private static readonly Dictionary<string, string> ClosingParticles = new(StringComparer.Ordinal)
    {
        ["deu"] = "ab an auf aus bei dar ein empor fort her herab heran herauf heraus herbei herein hernieder herüber herum hervor herzu "
                  + "hin hinab hinan hinauf hinaus hinein hinweg hinzu los mit nach nieder vor voran vorüber weg wieder zu zurück zusammen um",
    };

    private readonly record struct Word(string Folded, bool EndsSentence, bool EndsClause);

    private readonly Dictionary<string, Word[]> _verses;
    private readonly HashSet<string> _apart;
    private readonly HashSet<string> _closing;
    private readonly HashSet<string> _beside;
    private readonly HashSet<string> _joining;
    private readonly HashSet<(string Opening, string Closing)> _circumposed;

    private RenderingWords(Dictionary<string, Word[]> verses, string language)
    {
        _verses = verses;
        _apart = Set(WrittenApartWords, language);
        _closing = Set(ClosingParticles, language);
        _beside = Set(BesideWords, language);
        _joining = Set(JoiningWords, language);
        _circumposed = [.. Set(Circumpositions, language).Select(p => (p[..p.IndexOf('/')], p[(p.IndexOf('/') + 1)..]))];
    }

    private static HashSet<string> Set(Dictionary<string, string> lists, string language) =>
        lists.TryGetValue(language, out var words) ? [.. words.Split(' ', StringSplitOptions.RemoveEmptyEntries)] : [];

    /// <summary>A text's words as the competition reads them, or null where the store has no words of it.</summary>
    public static RenderingWords? Read(ProductStore store, string text)
    {
        if (!store.Exists(TextWord.ProductOf(text)) || !store.Exists(TextField.ProductOf(text)))
        {
            return null;
        }

        var language = Indexes.IndexContext.Declared(store, text).Language;
        using var reader = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
        return Of(language, reader.Rows().Select(w => (w.Key, w.Surface, w.Trailer)));
    }

    /// <summary>Words in key order, each with what the edition prints after it.</summary>
    internal static RenderingWords Of(string language, IEnumerable<(WordKey Key, string Surface, string Trailer)> words)
    {
        var verses = new Dictionary<string, Word[]>(StringComparer.Ordinal);
        var spellings = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var verse in words.GroupBy(w => w.Key.Verse))
        {
            var inVerse = verse.ToList();
            var placed = new Word[inVerse.Max(w => w.Key.Position)];
            foreach (var (key, surface, trailer) in inVerse)
            {
                var folded = Fold(surface);
                folded = spellings.TryGetValue(folded, out var known) ? known : spellings[folded] = folded;
                placed[key.Position - 1] = new Word(folded, trailer.AsSpan().IndexOfAny(SentenceEnds) >= 0,
                    trailer.AsSpan().IndexOfAny(ClauseEnds) >= 0);
            }

            verses[verse.Key.ToString()] = placed;
        }

        return new RenderingWords(verses, language);
    }

    private const string SentenceEnds = ".;:!?·";

    private const string ClauseEnds = ".,;:!?·";

    /// <summary>A word as written, lower case, without the punctuation or quotation marks an edition prints on it.</summary>
    internal static string Fold(string surface)
    {
        var span = surface.AsSpan();
        while (span.Length > 0 && !char.IsLetter(span[0]))
        {
            span = span[1..];
        }

        while (span.Length > 0 && !char.IsLetter(span[^1]) && span[^1] is not '’' and not '\'')
        {
            span = span[..^1];
        }

        return span.ToString().ToLowerInvariant();
    }

    /// <summary>
    /// Whether a standing link's translation words and another link's are one rendering of the original word both name.
    /// </summary>
    /// <param name="words">The translation's words, or null to judge by position alone.</param>
    /// <param name="givenElsewhere">Whether the word at a key between them is given by a surer link to another original word.</param>
    public static bool OneRendering(RenderingWords? words, IReadOnlyList<string> standing, IReadOnlyList<string> other, Func<string, bool> givenElsewhere)
    {
        string? verse = null;
        var positions = new List<int>(standing.Count + other.Count);
        foreach (var key in standing.Concat(other))
        {
            var dot = key.LastIndexOf('.');
            if (verse is null)
            {
                verse = key[..dot];
            }
            else if (!key.AsSpan(0, dot).SequenceEqual(verse))
            {
                return false;
            }

            positions.Add(int.Parse(key.AsSpan(dot + 1), CultureInfo.InvariantCulture));
        }

        positions.Sort();
        var (first, last) = (positions[0], positions[^1]);
        if (last - first + 1 == positions.Distinct().Count())
        {
            return true;
        }

        var placed = words?._verses.GetValueOrDefault(verse!);
        if (placed is not null && (SpeltAlike(placed, standing, other) || SentenceBetween(placed, first, last)))
        {
            return false;
        }

        if (placed is not null && (words!.Circumposed(placed, standing, other) || words.Circumposed(placed, other, standing)))
        {
            return true;
        }

        var bridged = true;
        for (var position = first + 1; position < last && bridged; position++)
        {
            if (positions.BinarySearch(position) < 0 && givenElsewhere(verse + "." + position.ToString(CultureInfo.InvariantCulture)))
            {
                bridged = false;
            }
        }

        if (bridged)
        {
            return true;
        }

        return placed is not null && !words!.Beside(placed, other) && !words.Beside(placed, standing)
               && (words.WrittenApart(placed, other) || words.WrittenApart(placed, standing));
    }

    private static Word At(Word[] placed, string key)
    {
        var position = int.Parse(key.AsSpan(key.LastIndexOf('.') + 1), CultureInfo.InvariantCulture);
        return position >= 1 && position <= placed.Length ? placed[position - 1] : default;
    }

    private static bool SpeltAlike(Word[] placed, IReadOnlyList<string> standing, IReadOnlyList<string> other)
    {
        var spelt = standing.Select(k => At(placed, k).Folded).Where(f => !string.IsNullOrEmpty(f)).ToHashSet(StringComparer.Ordinal);
        return other.Any(k => At(placed, k).Folded is { Length: > 0 } folded && spelt.Contains(folded));
    }

    private static bool SentenceBetween(Word[] placed, int first, int last)
    {
        for (var position = first; position < last && position <= placed.Length; position++)
        {
            if (placed[position - 1].EndsSentence)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a link's translation words are all conjunctions or articles, which hold no original word against another link.</summary>
    public bool Slight(IReadOnlyList<string> keys) =>
        keys.Count > 0 && _verses.GetValueOrDefault(keys[0][..keys[0].LastIndexOf('.')]) is { } placed
                       && keys.All(key => At(placed, key).Folded is { } folded && (_joining.Contains(folded) || _beside.Contains(folded)));

    private bool Circumposed(Word[] placed, IReadOnlyList<string> opening, IReadOnlyList<string> closing)
    {
        if (opening.Count != 1 || closing.Count != 1 || Position(opening[0]) >= Position(closing[0]))
        {
            return false;
        }

        return _circumposed.Contains((At(placed, opening[0]).Folded ?? string.Empty, At(placed, closing[0]).Folded ?? string.Empty));
    }

    private static int Position(string key) => int.Parse(key.AsSpan(key.LastIndexOf('.') + 1), CultureInfo.InvariantCulture);

    private bool Beside(Word[] placed, IReadOnlyList<string> keys) => keys.All(key => At(placed, key).Folded is { } folded && _beside.Contains(folded));

    private bool WrittenApart(Word[] placed, IReadOnlyList<string> keys) => keys.All(key =>
    {
        var word = At(placed, key);
        if (word.Folded is null)
        {
            return false;
        }

        if (_apart.Contains(word.Folded))
        {
            return true;
        }

        var position = int.Parse(key.AsSpan(key.LastIndexOf('.') + 1), CultureInfo.InvariantCulture);
        return _closing.Contains(word.Folded) && (word.EndsClause || position == placed.Length);
    });
}
