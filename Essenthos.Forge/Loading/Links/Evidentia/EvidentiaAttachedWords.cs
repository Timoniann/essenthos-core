namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>What a grammatical word of a translation is attached as.</summary>
internal enum EvidentiaAttachment
{
    None,

    /// <summary><em>the</em> of <em>the waters</em>.</summary>
    Article,

    /// <summary><em>and</em> before the word it joins on.</summary>
    Conjunction,

    /// <summary><em>in</em> of <em>in the beginning</em>.</summary>
    Preposition,

    /// <summary><em>of</em> of <em>the face of the deep</em>, which Hebrew writes as a construct chain.</summary>
    Genitive,

    /// <summary><em>will</em> of <em>will send</em>.</summary>
    AuxiliaryVerb,

    /// <summary><em>he</em> of <em>he said</em>, which a Hebrew or Greek verb carries in its ending.</summary>
    SubjectPronoun,

    /// <summary><em>his</em> of <em>his sons</em>, which Hebrew writes as a suffix of the noun.</summary>
    PossessivePronoun,

    /// <summary><em>him</em> of <em>struck him</em> or <em>to him</em>, a suffix of the verb or of the preposition.</summary>
    ObjectPronoun,

    /// <summary><em>to</em> of <em>to separate</em>, which Hebrew writes as ל before the infinitive.</summary>
    Infinitive,

    /// <summary>
    /// <em>was</em> of <em>there was light</em> or <em>was good</em>, a form of <em>be</em> that is no
    /// other verb's auxiliary.
    /// </summary>
    Copula,

    /// <summary><em>there</em> of <em>there was</em>, which says nothing of its own.</summary>
    Expletive,

    /// <summary><em>because</em>, <em>that</em> or <em>if</em>, which opens the clause of the verb it belongs to.</summary>
    Subordinator,

    /// <summary><em>Then</em>, <em>So</em>, <em>Now</em>, <em>But</em> or <em>Yet</em> opening a sentence, on the conjunction of its verb.</summary>
    SentenceConjunction,

    /// <summary><em>O</em> of <em>O LORD</em>, which the original says with the name itself.</summary>
    Vocative,

    /// <summary><em>out</em> of <em>went out</em>: a particle of direction, which the original's verb carries in its meaning.</summary>
    PhrasalParticle,

    /// <summary><em>went</em> of <em>went out</em>, where the particle was placed on the verb and the verb was not.</summary>
    VerbOfItsParticle,

    /// <summary><em>of</em> of <em>A Psalm of David</em>, on the ל written before the noun it governs.</summary>
    GenitivePreposition,

    /// <summary><em>Let</em> of <em>Let there be light</em>, which the original writes in its jussive <em>be</em>.</summary>
    LetBe,

    /// <summary><em>surely</em> of <em>you will surely die</em>, which Hebrew writes as the infinitive absolute beside the verb.</summary>
    InfinitiveAbsolute,

    /// <summary>
    /// <em>in</em> of <em>in his vineyard</em> or <em>to</em> of <em>to all the kings</em>, read with the noun it
    /// governs when the word after it was a possessive or a quantifier the original writes elsewhere.
    /// </summary>
    PrepositionOfPhrase,

    /// <summary>
    /// <em>and</em> of <em>and his voice</em> or <em>But</em> of <em>But you, Daniel</em>, on the ו written on
    /// the word of the first placed word after it, where its own rule found no placed word to follow.
    /// </summary>
    PrefixConjunction,

    /// <summary>
    /// <em>to</em> of <em>to everlasting shame</em> or <em>as</em> of <em>as the great owl</em>, on the ל or כ
    /// written on the word of the noun its modifiers stand before.
    /// </summary>
    PrefixPreposition,
}

/// <summary>Where an attached word goes once the word it belongs to has been placed.</summary>
internal enum EvidentiaAttachmentPlacement
{
    /// <summary>Not placed: the annotations measured against do not agree on a place for it.</summary>
    None,

    /// <summary>On the rendering of the word it belongs to.</summary>
    Rendering,

    /// <summary>On the word of its own kind written directly before that rendering, and nowhere if there is none.</summary>
    OwnKindBefore,

    /// <summary>On the preposition written before the first rendering of its phrase's words.</summary>
    PhraseStart,

    /// <summary>
    /// On the rendering of the governed noun, when the word before <em>of</em> was placed on the Hebrew
    /// noun in the construct state it depends on.
    /// </summary>
    Dependent,

    /// <summary>
    /// On the rendering when its ending or suffix names the same person, number and gender as the
    /// pronoun; a pronoun object of an English preposition goes on the preposition written after the
    /// verb, when its suffix names it.
    /// </summary>
    AgreeingRendering,

    /// <summary>On the preposition written directly before an infinitive, or on the infinitive where there is none.</summary>
    Infinitive,

    /// <summary>On the original's own verb <em>be</em>, written directly beside the rendering of the word it links.</summary>
    BeBeside,

    /// <summary>On a conjunction other than <em>and</em> written directly before the rendering of the clause's verb.</summary>
    ConjunctionBefore,

    /// <summary>
    /// On the conjunction that opens the clause of the rendering: the ו written before it in Hebrew, and
    /// in Greek the καί, δέ, οὖν, τότε or ἀλλά standing before it in the same clause.
    /// </summary>
    ClauseConjunction,

    /// <summary>On the ל written before the rendering of the governed noun, past an article.</summary>
    LamedBefore,

    /// <summary>On the rendering, where it is the original's own <em>be</em>.</summary>
    BeVerb,

    /// <summary>On the infinitive absolute of the rendering's own lexeme, written beside it.</summary>
    InfinitiveAbsolute,
}

/// <summary>
/// Places grammatical words no lexical tier attempts by the word they belong to. Nothing here is
/// evidence that two words correspond: the placement follows the proposal of the head word, and a
/// head left unplaced leaves its grammatical words unplaced too.
///
/// <para>Each English word goes on its own part of the written word. BHSA writes the article, the
/// prefixed prepositions and ו as words of their own, and <em>the</em> goes on the article, spelt out or
/// swallowed by a preposition, where the answer keys put it with the noun; a noun the original writes
/// without one leaves <em>the</em> to be said supplied. <em>and</em> goes on the ו before the word it
/// joins; an auxiliary goes on the verb, which carries tense and mood in itself.</para>
///
/// <para>A personal pronoun goes where the original writes the person: a subject on the verb whose
/// ending names it, a possessive on the Hebrew noun whose suffix does, an object on the suffix of the
/// verb or preposition. The morphology decides, not the habit: a pronoun whose head was placed on a
/// word that names another person, or none, is left unplaced, and so is a subject the original writes
/// as a pronoun of its own beside the verb, since both annotations put it there.</para>
///
/// <para>An English preposition goes on the preposition written before the rendering of the word it
/// governs, past an article - BHSA writes <em>בְּ</em> of <em>בְּרֵאשִׁית</em> as a word of its own -
/// or on that rendering itself where it is a preposition carrying the pronoun (<em>to him</em>, לוֹ).
/// Not <em>of</em>, which Hebrew writes as the construct state of the noun before it, and not the
/// particles of phrasal verbs (<em>out of</em>, <em>up</em>); and never the object marker את, which
/// no English preposition renders.</para>
///
/// <para>Against Hebrew <em>of</em> goes on the rendering of the noun it governs, and only where the
/// word before it was placed on a noun in the construct state standing before that rendering, with
/// nothing but an article between. Against Greek it is not placed: the annotations put <em>of</em> of
/// a genitive on its article as often as on its noun.</para>
///
/// <para>Against Greek only <em>and</em>, <em>the</em>, the preposition and the subject pronoun are
/// placed: <em>and</em> on the <em>καί</em> written directly before the rendering, which both
/// annotations do; <em>the</em> on the article of its noun, past an adjective between them, which is
/// where the Berean tables put it; the preposition on the preposition before the rendering; and the
/// subject on the verb whose ending names it.</para>
///
/// <para>A word that opens a sentence with <em>Then</em>, <em>So</em>, <em>Now</em>, <em>But</em> or
/// <em>Yet</em> renders the conjunction of its clause's verb - the ו of וַיֹּאמֶר, the καί or δέ of the
/// Greek - and goes there only where the two stand alike: both first in their verse, or neither. A
/// word first in the English verse against a ו further in is as often a reordered clause, whose
/// conjunction another word renders; the postpositive δέ is never first and is exempt.</para>
///
/// <para>Some words go on a word another already holds, where the original says both in one: <em>O</em>
/// with the name it calls, <em>Let</em> of <em>Let there be</em> with the jussive היה, a particle of
/// direction with its verb (<em>went out</em>, יָצָא), and the verb with its particle where only the
/// particle was placed. <em>of</em> before a noun whose rendering has a ל written before it goes on the
/// ל, and <em>surely</em> on the infinitive absolute of its verb's own lexeme.</para>
///
/// <para>Not placed, because on these texts they measured below the precision the lexical tiers
/// already reach: <em>up</em> and the other idiomatic particles; a Greek auxiliary, which is often a
/// periphrastic <em>ἦσαν</em> of its own rather than part of the verb after it; an auxiliary on the
/// negation after it (<em>will not</em>, 89-91%); a copula on its predicate where the original writes
/// no <em>be</em> (81-83%); <em>there is</em> on אֵין (86%); and Greek <em>of</em> on its genitive
/// noun (55-64%).</para>
///
/// <para>Last, when every other attached word has its place, a coordinator, <em>to</em>, <em>as</em> or
/// <em>like</em> still unplaced goes on the prefix of its own kind written on the word of the first
/// placed word after it (<see cref="AttachToPrefixes"/>).</para>
///
/// <para>All of that is English. German and Spanish attach two kinds only, the two that write a verb's
/// inflection as a word: the subject pronoun and the auxiliary of tense, by the German and Spanish
/// UDPipe parses (<see cref="ClassifyInflection"/>). Every other source language is left alone.</para>
/// </summary>
internal static class EvidentiaAttachedWords
{
    private const double ConfidenceBelowHead = 0.10;

    private const int HeadReach = 3;

    /// <summary>How many words a preposition may stand before the noun it governs: <em>for all the glorious things</em>.</summary>
    private const int PhraseReach = 4;

    private static readonly HashSet<string> Quantifiers = new(StringComparer.OrdinalIgnoreCase) { "all", "every", "each", "any" };

    private const string EnglishLanguage = "eng";

    private const string GermanLanguage = "deu";

    private const string SpanishLanguage = "spa";

    /// <summary>
    /// The auxiliaries of German and Spanish that write an original verb's tense, mood or voice as a word of
    /// their own: <em>wird sagen</em>, <em>hat gesagt</em>, <em>ist gekommen</em>, <em>sollst töten</em>,
    /// <em>ha dicho</em>, <em>fue hecho</em>, each with the forms of the verb it is the tense of: with
    /// any other it is a verb of its own, as in <em>ich hatte euch viel zu schreiben</em>, or the parse
    /// hung it on the verb of another clause. <em>ha de morir</em> is Spanish's own future. The modals
    /// that are words of their own in the original - <em>können</em>, <em>poder</em> for יכל and δύναμαι -
    /// are not here.
    /// </summary>
    private static readonly Dictionary<string, string[]> InflectionAuxiliaries = new(StringComparer.OrdinalIgnoreCase)
    {
        ["werden"] = [ParticipleForm, InfinitiveForm],
        ["haben"] = [ParticipleForm],
        ["sein"] = [ParticipleForm],
        ["sollen"] = [InfinitiveForm],
        ["haber"] = [ParticipleForm, InfinitiveForm],
        ["ser"] = [ParticipleForm],
    };

    private const string ParticipleForm = "Part";

    private const string InfinitiveForm = "Inf";

    private const string PassiveSubjectRelation = "nsubj:pass";

    private const string GreekBe = "G1510";

    /// <summary>μέλλω, <em>be about to</em>.</summary>
    private const string GreekAboutTo = "G3195";

    private const string HebrewBe = "H1961";

    /// <summary>BHSA's active and passive participle.</summary>
    private static readonly HashSet<string> HebrewParticiples = new(StringComparer.OrdinalIgnoreCase) { "ptca", "ptcp" };

    private const int OwnAuxiliaryReach = 2;

    private const string NeuterGender = "Neut";

    private const string HebrewLanguage = "hbo";

    private const string GreekLanguage = "grc";

    /// <summary>
    /// Greek's <em>and</em>. Its other conjunctions standing before a word subordinate rather than join
    /// (<em>εἰ</em>, <em>ὅτι</em>), and <em>δέ</em> is never written first.
    /// </summary>
    private const string GreekAnd = "καί";

    private const string DefiniteArticle = "the";

    private static readonly HashSet<string> Articles = new(StringComparer.OrdinalIgnoreCase) { DefiniteArticle, "a", "an" };

    private static readonly HashSet<string> Conjunctions = new(StringComparer.OrdinalIgnoreCase) { "and" };

    /// <summary>Case-sensitive: only the word that opens a sentence says <em>Then</em> rather than <em>then</em>.</summary>
    private static readonly HashSet<string> SentenceConjunctions = new(StringComparer.Ordinal) { "Then", "So", "Now", "But", "Yet" };

    /// <summary>καί, δέ, οὖν, τότε and ἀλλά.</summary>
    private static readonly HashSet<string> GreekClauseConjunctions = new(StringComparer.Ordinal) { "G2532", "G1161", "G3767", "G5119", "G235" };

    /// <summary>δέ, which stands second in its clause and so never first in a verse.</summary>
    private const string Postpositive = "G1161";

    /// <summary>How far before the rendering a Greek clause conjunction may stand without another verb between.</summary>
    private const int ClauseConjunctionReach = 4;

    private const string Vocative = "O";

    private const string Let = "let";

    /// <summary>The particles of direction a Hebrew or Greek verb of motion carries in itself; <em>up</em> is idiom as often.</summary>
    private static readonly HashSet<string> DirectionalParticles = new(StringComparer.OrdinalIgnoreCase) { "out", "down", "off", "away", "forth" };

    private const string ParticleRelation = "compound:prt";

    /// <summary>The adverbs English writes for the Hebrew infinitive absolute that strengthens a verb.</summary>
    private static readonly HashSet<string> Emphatic = new(StringComparer.OrdinalIgnoreCase) { "surely", "certainly", "indeed", "utterly", "fully" };

    private const string InfinitiveAbsoluteTense = "infa";

    private const string Lamed = "H9005";

    private const string Kaph = "H9004";

    private const string Vav = "H9000";

    /// <summary>The coordinators that reach for the ו written on the word of the first placed word after them.</summary>
    private static readonly HashSet<string> PrefixConjunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "but", "or", "nor", "yet", "while",
    };

    /// <summary>The prepositions that reach for the prefix of their own kind, and the prefix each renders.</summary>
    private static readonly Dictionary<string, string> PrefixPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        [To] = Lamed, ["as"] = Kaph, ["like"] = Kaph,
    };

    /// <summary>
    /// How far after a coordinator the first placed word may stand: <em>and then he was no more</em>. On the
    /// passages the rules were chosen on a fifth word added two placements, one of them wrong.
    /// </summary>
    private const int ConjunctionPrefixReach = 4;

    /// <summary>How far after <em>to</em> or <em>as</em> its noun may stand, past its modifiers: <em>to his own place</em>.</summary>
    private const int PrepositionPrefixReach = 3;

    private const string Of = "of";

    private static readonly HashSet<string> UnplacedPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        Of, "out", "up", "off", "as", "down", "away", "forth",
    };

    private const string HebrewArticle = "H9009";

    /// <summary>The Hebrew object marker, which BHSA classes as a preposition.</summary>
    private const string ObjectMarker = "H853";

    /// <summary>BHSA's construct state, the noun that the next one depends on.</summary>
    private const string ConstructState = "c";

    private static readonly HashSet<string> Negations = new(StringComparer.OrdinalIgnoreCase) { "not", "n't", "never" };

    /// <summary>How far after its verb the preposition carrying an object pronoun may stand: <em>וַיֹּאמֶר לוֹ</em>.</summary>
    private const int ObjectPrepositionReach = 2;

    /// <summary>How far from the verb an original writes a subject pronoun of its own: <em>וְהוּא יִמְשָׁל</em>.</summary>
    private const int SubjectPronounReach = 3;

    private const string To = "to";

    private const string Be = "be";

    private const string There = "there";

    /// <summary>The verb <em>be</em> of each original: היה, εἰμί and γίνομαι.</summary>
    private static readonly HashSet<string> BeVerbs = new(StringComparer.Ordinal) { "H1961", "G1510", "G1096" };

    /// <summary>The relations by which a form of <em>be</em> is the tense of another verb rather than a verb of its own.</summary>
    private static readonly HashSet<string> AuxiliaryRelations = new(StringComparer.Ordinal) { "aux", "aux:pass" };

    private const string ExpletiveRelation = "expl";

    private const string SubjectRelation = "nsubj";

    private const string CopulaRelation = "cop";

    private const string MarkerRelation = "mark";

    private const string SubordinatingConjunction = "SCONJ";

    /// <summary>
    /// The original's words for <em>and</em>: ו, καί and δέ. They open a clause as often as they join a
    /// word, and English writes them as <em>and</em>, which is placed by its own rule.
    /// </summary>
    private static readonly HashSet<string> Coordinators = new(StringComparer.Ordinal) { "H9000", "G2532", "G1161" };

    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> placed)
    {
        var placedBySource = placed
            .GroupBy(proposal => proposal.Source.Token.Id)
            .ToDictionary(group => group.Key, group => group.First());
        var targetsByVerse = target
            .DistinctBy(analysis => analysis.Token.Id)
            .GroupBy(analysis => analysis.Token.Address)
            .ToDictionary(group => group.Key, group => group.OrderBy(analysis => analysis.Token.Position).ToList());
        var taken = placed.Select(proposal => proposal.Target.Token.Id).ToHashSet();
        var proposals = new List<EvidentiaProposal>();
        var verses = source
            .Where(analysis => Attaches(analysis.Token.Language))
            .DistinctBy(analysis => analysis.Token.Id)
            .GroupBy(analysis => analysis.Token.Address)
            .Select(verse => verse.OrderBy(analysis => analysis.Token.Position).ToList())
            .ToList();
        // A word can belong to one that is itself attached - and of and he said, to of to him - so the
        // pass repeats until it places nothing new.
        for (var placing = true; placing;)
        {
            placing = false;
            foreach (var words in verses)
            {
                placing |= Attach(words, placedBySource, targetsByVerse, taken, proposals);
            }
        }

        foreach (var words in verses)
        {
            AttachToPrefixes(words, placedBySource, targetsByVerse, taken, proposals);
        }

        return proposals;
    }

    private static bool Attach(
        IReadOnlyList<EvidentiaAnalysis> words,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        HashSet<long> taken,
        List<EvidentiaProposal> proposals)
    {
        var attached = false;
        for (var index = 0; index < words.Count; index++)
        {
            if (placedBySource.ContainsKey(words[index].Token.Id))
            {
                continue;
            }

            var (classified, classifiedHead) = Classify(words, index);
            var attachment = classified ?? EvidentiaAttachment.None;
            var head = classifiedHead!;
            var found = classified is { } primary && classifiedHead is { } primaryHead
                ? Placed(primary, index, primaryHead, words, placedBySource, targetsByVerse, taken)
                : null;
            if (found is null && attachment == EvidentiaAttachment.Genitive
                && Placed(EvidentiaAttachment.GenitivePreposition, index, head, words, placedBySource, targetsByVerse, taken) is { } lamed)
            {
                found = lamed;
                attachment = EvidentiaAttachment.GenitivePreposition;
            }

            if (found is null)
            {
                foreach (var (other, otherHead) in Fallbacks(words, index))
                {
                    if (Placed(other, index, otherHead, words, placedBySource, targetsByVerse, taken) is { } placedOther)
                    {
                        found = placedOther;
                        attachment = other;
                        head = otherHead;
                        break;
                    }
                }
            }

            if (found is { } conjunction && attachment == EvidentiaAttachment.SentenceConjunction
                && !StandsAlike(index, conjunction.Placement, targetsByVerse))
            {
                found = null;
            }

            if (found is null && attachment == EvidentiaAttachment.Conjunction
                && Syntactic(words, words[index].Token.SyntacticHead) is { } clause && clause.Token.Id != head.Token.Id
                && Placed(attachment, index, clause, words, placedBySource, targetsByVerse, taken) is { } onTheVerb
                && OpensTheSentence(words[index]) == onTheVerb.Placement.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
            {
                found = onTheVerb;
            }

            if (found is not { } result || EvidentiaTenseAgreement.Disagrees(words[index], result.Placement))
            {
                continue;
            }

            var (placement, headProposal) = result;

            taken.Add(placement.Token.Id);
            var proposal = new EvidentiaProposal(
                words[index],
                placement,
                EvidentiaProposalKind.AttachedWord,
                Math.Max(0, headProposal.Confidence - ConfidenceBelowHead),
                new EvidentiaDecisionTrace(
                    "attached",
                    $"{attachment} of '{head.Token.Surface}'{ParseCredit(words[index])}",
                    headProposal.Trace?.Evidence ?? []),
                headProposal);
            proposals.Add(proposal);
            placedBySource[words[index].Token.Id] = proposal;
            attached = true;
        }

        return attached;
    }

    /// <summary>
    /// A coordinator, <em>to</em>, <em>as</em> or <em>like</em> that its own rule left unplaced, on the prefix
    /// of its kind written on the word of the first placed word after it: <em>and his voice</em> on the ו of
    /// וְקוֹלוֹ, <em>But you, Daniel</em> on the ו of וְאַתָּה, <em>to everlasting shame</em> on the ל of
    /// לַחֲרָפוֹת. Its own rule reads it with one word, within three, and that word is as often a
    /// possessive, an adjective, or a subject the original writes in the verb.
    ///
    /// <para>The prefix must be written on the very word the placed word renders, free, and after the
    /// rendering of the last word placed before it: a ו standing earlier belongs to a clause the English
    /// reordered. Nothing is read past punctuation or another coordinator. A coordinator reaches across
    /// any words, since the ו opens the clause whatever English puts first; a preposition only across
    /// the words that modify its noun, since across a verb <em>to</em> belongs to the infinitive.</para>
    /// </summary>
    private static void AttachToPrefixes(
        IReadOnlyList<EvidentiaAnalysis> words,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        HashSet<long> taken,
        List<EvidentiaProposal> proposals)
    {
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            var conjunction = PrefixConjunctions.Contains(word.Token.Surface);
            if (placedBySource.ContainsKey(word.Token.Id) || !IsEnglish(word) || Punctuated(word)
                || !conjunction && !PrefixPrepositions.ContainsKey(word.Token.Surface))
            {
                continue;
            }

            var prefixNumber = conjunction ? Vav : PrefixPrepositions[word.Token.Surface];
            var reach = conjunction ? ConjunctionPrefixReach : PrepositionPrefixReach;
            for (var next = index + 1; next < words.Count && next - index <= reach; next++)
            {
                var other = words[next];
                if (PrefixConjunctions.Contains(other.Token.Surface))
                {
                    break;
                }

                if (placedBySource.TryGetValue(other.Token.Id, out var head))
                {
                    if (head.Target.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
                        && targetsByVerse.TryGetValue(head.Target.Token.Address, out var verse)
                        && PrefixOfItsWord(head.Target, verse, prefixNumber) is { } prefix
                        && !taken.Contains(prefix.Token.Id)
                        && StandsAfterTheWordBefore(words, index, prefix, placedBySource))
                    {
                        var attachment = conjunction ? EvidentiaAttachment.PrefixConjunction : EvidentiaAttachment.PrefixPreposition;
                        taken.Add(prefix.Token.Id);
                        var proposal = new EvidentiaProposal(
                            word,
                            prefix,
                            EvidentiaProposalKind.AttachedWord,
                            Math.Max(0, head.Confidence - ConfidenceBelowHead),
                            new EvidentiaDecisionTrace("attached", $"{attachment} of '{other.Token.Surface}'", head.Trace?.Evidence ?? []),
                            head);
                        proposals.Add(proposal);
                        placedBySource[word.Token.Id] = proposal;
                    }

                    break;
                }

                if (Punctuated(other) || !conjunction && !IsModifier(other))
                {
                    break;
                }
            }
        }
    }

    /// <summary>The prefix of that number among the parts written on the rendering's own word, before it: וּ of וּלְבֵיתוֹ.</summary>
    private static EvidentiaAnalysis? PrefixOfItsWord(EvidentiaAnalysis rendering, IReadOnlyList<EvidentiaAnalysis> verse, string number)
    {
        for (var before = IndexOf(verse, rendering) - 1;
             before >= 0 && verse[before].Token.Trailer.Length == 0 && Class(verse[before]) is "det" or "adp" or "conj";
             before--)
        {
            if (verse[before].Token.StrongNumber == number)
            {
                return verse[before];
            }
        }

        return null;
    }

    private static bool StandsAfterTheWordBefore(
        IReadOnlyList<EvidentiaAnalysis> words,
        int index,
        EvidentiaAnalysis prefix,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource)
    {
        for (var before = index - 1; before >= 0; before--)
        {
            if (placedBySource.TryGetValue(words[before].Token.Id, out var earlier))
            {
                return !earlier.Target.Token.Address.Equals(prefix.Token.Address)
                    || earlier.Target.Token.Position < prefix.Token.Position;
            }
        }

        return true;
    }

    /// <summary>A word that stands between a preposition and its noun: an article, an adjective, a numeral or a possessive.</summary>
    private static bool IsModifier(EvidentiaAnalysis word) =>
        Class(word) is "det" or "adj" or "num" || Class(word) == "pron" && Feature(word, "Poss") == "Yes";

    /// <summary>
    /// <em>And</em> of <em>And God said</em> stands before the subject, and the וַ it renders is written on
    /// the verb before it: וַיֹּאמֶר אֱלֹהִים. Where the head it was read with has no conjunction before its
    /// rendering, <em>and</em> goes on the one before the rendering of its clause's verb - in Hebrew when it
    /// opens a sentence, where the ו of a narrative verb stands; in Greek when it does not, since a
    /// Greek sentence opens as often with δέ, which is no <em>and</em> this places.
    /// </summary>
    private static bool OpensTheSentence(EvidentiaAnalysis word) => char.IsUpper(word.Token.Surface[0]);

    private static bool StandsAlike(
        int index,
        EvidentiaAnalysis conjunction,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse) =>
        conjunction.Token.StrongNumber == Postpositive
        || targetsByVerse.TryGetValue(conjunction.Token.Address, out var verse) && (index == 0) == (IndexOf(verse, conjunction) == 0);

    /// <summary>
    /// What a word left unplaced by its own kind may still attach as: each is tried in turn and the
    /// first that finds a place is taken, so none of them displaces an attachment the word already has.
    /// </summary>
    private static IEnumerable<(EvidentiaAttachment, EvidentiaAnalysis)> Fallbacks(IReadOnlyList<EvidentiaAnalysis> words, int index)
    {
        var word = words[index];
        if (!IsEnglish(word))
        {
            yield break;
        }

        if (word.Token.Surface.Equals(Let, StringComparison.OrdinalIgnoreCase))
        {
            for (var next = index + 1; next < words.Count && next - index <= 2; next++)
            {
                if (words[next].Lemma == Be)
                {
                    yield return (EvidentiaAttachment.LetBe, words[next]);
                    break;
                }
            }
        }

        if (Emphatic.Contains(word.Token.Surface)
            && Syntactic(words, word.Token.SyntacticHead) is { } emphasised && Class(emphasised) == "verb")
        {
            yield return (EvidentiaAttachment.InfinitiveAbsolute, emphasised);
        }

        if (word.Token.Relation == ParticleRelation && DirectionalParticles.Contains(word.Token.Surface)
            && Syntactic(words, word.Token.SyntacticHead) is { } verb && Class(verb) == "verb")
        {
            yield return (EvidentiaAttachment.PhrasalParticle, verb);
        }

        if (Class(word) == "verb"
            && words.FirstOrDefault(other => other.Token.SyntacticHead == word.Token.Id && other.Token.Relation == ParticleRelation) is { } particle)
        {
            yield return (EvidentiaAttachment.VerbOfItsParticle, particle);
        }

        if (Class(word) == "adp" && !UnplacedPrepositions.Contains(word.Token.Surface) && !OpensTheSentence(word)
            && Syntactic(words, word.Token.SyntacticHead) is { } governed && Class(governed) is "noun" or "propn"
            && IndexOf(words, governed) is var at && at > index && at - index <= PhraseReach
            && !words.Skip(index).Take(at - index).Any(Punctuated))
        {
            yield return (EvidentiaAttachment.PrepositionOfPhrase, governed);
        }
    }

    private static (EvidentiaAnalysis Placement, EvidentiaProposal Head)? Placed(
        EvidentiaAttachment attachment,
        int index,
        EvidentiaAnalysis head,
        IReadOnlyList<EvidentiaAnalysis> words,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        HashSet<long> taken) =>
        HeadProposal(attachment, index, head, words, placedBySource) is ({ } headProposal, var afterVerb)
        && targetsByVerse.TryGetValue(headProposal.Target.Token.Address, out var targetVerse)
        && Place(attachment, words, index, headProposal.Target, targetVerse, afterVerb, placedBySource, taken) is { } placement
            ? (placement, headProposal)
            : null;

    /// <summary>
    /// The attachments that inherit the safe tier from their head. Each placed the word where the answer
    /// keys put it at least as often as the safe tier's own renderings do, on the passages the rules
    /// were chosen on and on those they were not: an article, the <em>of</em> of a construct chain and a
    /// possessive written as the suffix of the noun. The others - a conjunction, a preposition, a subject
    /// or object pronoun and an auxiliary - stay in review however safe their head is.
    /// </summary>
    private static readonly HashSet<EvidentiaAttachment> SafeWithTheirHead =
    [
        EvidentiaAttachment.Article,
        EvidentiaAttachment.Genitive,
        EvidentiaAttachment.PossessivePronoun,
    ];

    /// <summary>
    /// The safe tier once attached words are counted with it: the safe renderings, and every attached word
    /// of a kind that inherits the tier whose head is itself in it.
    /// </summary>
    public static IReadOnlySet<(long From, long To)> Safe(
        IReadOnlyList<EvidentiaProposal> safe,
        IReadOnlyList<EvidentiaProposal> attached)
    {
        var pairs = safe.Select(proposal => (proposal.Source.Token.Id, proposal.Target.Token.Id)).ToHashSet();
        // In the order they were attached, so a word attached to an attached word finds its head decided.
        foreach (var proposal in attached)
        {
            if (proposal.Head is { } head
                && pairs.Contains((head.Source.Token.Id, head.Target.Token.Id))
                && Attachment(proposal) is { } attachment
                && SafeWithTheirHead.Contains(attachment))
            {
                pairs.Add((proposal.Source.Token.Id, proposal.Target.Token.Id));
            }
        }

        return pairs;
    }

    internal static EvidentiaAttachment? Attachment(EvidentiaProposal proposal) =>
        proposal.Trace?.Rationale.Split(' ', 2)[0] is { } name && Enum.TryParse<EvidentiaAttachment>(name, out var attachment)
            ? attachment
            : null;

    internal static EvidentiaAttachmentPlacement Placement(
        EvidentiaAttachment attachment,
        string witnessLanguage,
        string sourceLanguage = EnglishLanguage) =>
        !sourceLanguage.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase)
            ? InflectionPlacement(attachment, witnessLanguage)
            : witnessLanguage.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            ? attachment switch
            {
                EvidentiaAttachment.Article or EvidentiaAttachment.Conjunction or EvidentiaAttachment.Preposition =>
                    EvidentiaAttachmentPlacement.OwnKindBefore,
                EvidentiaAttachment.PrepositionOfPhrase => EvidentiaAttachmentPlacement.PhraseStart,
                EvidentiaAttachment.SubjectPronoun => EvidentiaAttachmentPlacement.AgreeingRendering,
                EvidentiaAttachment.Infinitive => EvidentiaAttachmentPlacement.Infinitive,
                EvidentiaAttachment.Copula => EvidentiaAttachmentPlacement.BeBeside,
                EvidentiaAttachment.Expletive => EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.Subordinator => EvidentiaAttachmentPlacement.ConjunctionBefore,
                EvidentiaAttachment.SentenceConjunction => EvidentiaAttachmentPlacement.ClauseConjunction,
                EvidentiaAttachment.Vocative or EvidentiaAttachment.PhrasalParticle or EvidentiaAttachment.VerbOfItsParticle =>
                    EvidentiaAttachmentPlacement.Rendering,
                _ => EvidentiaAttachmentPlacement.None,
            }
            : !witnessLanguage.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
            ? EvidentiaAttachmentPlacement.None
            : attachment switch
            {
                EvidentiaAttachment.AuxiliaryVerb => EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.Article or EvidentiaAttachment.Conjunction or EvidentiaAttachment.Preposition =>
                    EvidentiaAttachmentPlacement.OwnKindBefore,
                EvidentiaAttachment.PrepositionOfPhrase => EvidentiaAttachmentPlacement.PhraseStart,
                EvidentiaAttachment.Genitive => EvidentiaAttachmentPlacement.Dependent,
                EvidentiaAttachment.SubjectPronoun or EvidentiaAttachment.PossessivePronoun or EvidentiaAttachment.ObjectPronoun =>
                    EvidentiaAttachmentPlacement.AgreeingRendering,
                EvidentiaAttachment.Infinitive => EvidentiaAttachmentPlacement.Infinitive,
                EvidentiaAttachment.Copula => EvidentiaAttachmentPlacement.BeBeside,
                EvidentiaAttachment.Expletive => EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.Subordinator => EvidentiaAttachmentPlacement.ConjunctionBefore,
                EvidentiaAttachment.SentenceConjunction => EvidentiaAttachmentPlacement.ClauseConjunction,
                EvidentiaAttachment.Vocative or EvidentiaAttachment.PhrasalParticle or EvidentiaAttachment.VerbOfItsParticle =>
                    EvidentiaAttachmentPlacement.Rendering,
                EvidentiaAttachment.GenitivePreposition => EvidentiaAttachmentPlacement.LamedBefore,
                EvidentiaAttachment.LetBe => EvidentiaAttachmentPlacement.BeVerb,
                EvidentiaAttachment.InfinitiveAbsolute => EvidentiaAttachmentPlacement.InfinitiveAbsolute,
                _ => EvidentiaAttachmentPlacement.None,
            };

    /// <summary>What a word attaches as, and the word it belongs to; both null when it is not one of these.</summary>
    internal static (EvidentiaAttachment? Attachment, EvidentiaAnalysis? Head) Classify(
        IReadOnlyList<EvidentiaAnalysis> words,
        int index)
    {
        var word = words[index];
        if (!IsEnglish(word))
        {
            return ClassifyInflection(words, word);
        }

        var partOfSpeech = Class(word);
        if (partOfSpeech == "det" && Articles.Contains(word.Token.Surface))
        {
            return Found(EvidentiaAttachment.Article, Forward(words, index, "noun", "propn", "adj", "num"));
        }

        if (partOfSpeech == "conj" && Conjunctions.Contains(word.Token.Surface))
        {
            return Found(EvidentiaAttachment.Conjunction,
                Forward(words, index, "noun", "propn", "pron", "verb", "adj", "adv", "num"));
        }

        if (word.Token.Surface == Vocative)
        {
            return Found(EvidentiaAttachment.Vocative, Forward(words, index, "propn", "noun"));
        }

        if (partOfSpeech is "adv" or "conj" && SentenceConjunctions.Contains(word.Token.Surface))
        {
            var clause = Syntactic(words, word.Token.SyntacticHead);
            return Found(EvidentiaAttachment.SentenceConjunction,
                clause is not null && Class(clause) == "verb" ? clause : Forward(words, index, "verb"));
        }

        if (partOfSpeech == "conj" && word.Token.Relation == MarkerRelation
            && string.Equals(word.PartOfSpeech ?? word.Token.PartOfSpeech, SubordinatingConjunction, StringComparison.OrdinalIgnoreCase))
        {
            return Found(EvidentiaAttachment.Subordinator, Syntactic(words, word.Token.SyntacticHead));
        }

        if (partOfSpeech == "adp" && word.Token.Surface.Equals(Of, StringComparison.OrdinalIgnoreCase))
        {
            return Found(EvidentiaAttachment.Genitive, Forward(words, index, "noun", "propn", "adj", "num", "pron"));
        }

        if (partOfSpeech == "part" && word.Token.Surface.Equals(To, StringComparison.OrdinalIgnoreCase))
        {
            return Found(EvidentiaAttachment.Infinitive, Forward(words, index, "verb"));
        }

        if (partOfSpeech == "pron" && word.Token.Relation == ExpletiveRelation
            && word.Token.Surface.Equals(There, StringComparison.OrdinalIgnoreCase))
        {
            return Found(EvidentiaAttachment.Expletive, Syntactic(words, word.Token.SyntacticHead));
        }

        if (partOfSpeech is "aux" or "verb" && word.Lemma == Be && word.Token.Relation is { } relation
            && !AuxiliaryRelations.Contains(relation))
        {
            return Found(EvidentiaAttachment.Copula, CopulaAnchor(words, word));
        }

        if (partOfSpeech == "adp" && !UnplacedPrepositions.Contains(word.Token.Surface))
        {
            return Found(EvidentiaAttachment.Preposition,
                Forward(words, index, "noun", "propn", "pron", "adj", "num", "verb"));
        }

        if (partOfSpeech == "pron" && Feature(word, "PronType") == "Prs" && Feature(word, "Reflex") is null
            && EvidentiaPersonAgreement.Person(word) is { } person)
        {
            if (Feature(word, "Poss") == "Yes")
            {
                return Found(EvidentiaAttachment.PossessivePronoun, Forward(words, index, "noun", "propn", "adj"));
            }

            // You states no case and is read as the subject of a verb after it. It is never read as a
            // subject: as often as not it is the empty it of it happened, which the original does not
            // write at all.
            var subject = Feature(word, "Case") is { } stated
                ? stated.Equals("Nom", StringComparison.OrdinalIgnoreCase) && Feature(word, "Gender") != "Neut"
                : person == "2" && Forward(words, index, "verb") is not null;
            return subject
                ? Found(EvidentiaAttachment.SubjectPronoun, Forward(words, index, "verb"))
                : Found(EvidentiaAttachment.ObjectPronoun, Backward(words, index, "verb", "adp"));
        }

        return word.Role == EvidentiaAuxiliaryRole.AuxiliaryVerb
            ? Found(EvidentiaAttachment.AuxiliaryVerb, Forward(words, index, "verb"))
            : (null, null);
    }

    /// <summary>
    /// The proposal the attached word follows, and whether it goes after that proposal rather than on
    /// it: in <em>said to him</em> the <em>to</em> is never placed, and <em>him</em> goes on the
    /// preposition written after the word <em>said</em> was placed on.
    /// </summary>
    private static (EvidentiaProposal? Proposal, bool AfterVerb) HeadProposal(
        EvidentiaAttachment attachment,
        int index,
        EvidentiaAnalysis head,
        IReadOnlyList<EvidentiaAnalysis> words,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource)
    {
        // Greek writes the article before its noun and repeats it before an attributive adjective
        // (τὸ πνεῦμα τὸ ἀκάθαρτον), so the unclean spirit puts the on the noun's article.
        if (attachment == EvidentiaAttachment.Article && Class(head) is "adj" or "num"
            && Forward(words, index, "noun", "propn") is { } noun
            && placedBySource.TryGetValue(noun.Token.Id, out var nounProposal)
            && nounProposal.Target.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase))
        {
            return (nounProposal, false);
        }

        if (placedBySource.TryGetValue(head.Token.Id, out var proposal))
        {
            return (proposal, false);
        }

        if (attachment != EvidentiaAttachment.ObjectPronoun || Class(head) != "adp")
        {
            return (null, false);
        }

        var at = IndexOf(words, head);
        return at > 0 && Class(words[at - 1]) == "verb" && !Punctuated(words[at - 1])
            && placedBySource.TryGetValue(words[at - 1].Token.Id, out var verb)
                ? (verb, true)
                : (null, false);
    }

    private static EvidentiaAnalysis? Place(
        EvidentiaAttachment attachment,
        IReadOnlyList<EvidentiaAnalysis> words,
        int index,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        bool afterVerb,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlySet<long> taken)
    {
        var word = words[index];
        return Placement(attachment, rendering.Token.Language, word.Token.Language) switch
        {
            EvidentiaAttachmentPlacement.Rendering when attachment == EvidentiaAttachment.AuxiliaryVerb && !IsEnglish(word)
                && OwnAuxiliary(rendering, verse) is { } own =>
                taken.Contains(own.Token.Id) ? null : own,
            EvidentiaAttachmentPlacement.Rendering when attachment == EvidentiaAttachment.AuxiliaryVerb && !IsEnglish(word)
                && Class(rendering) != "verb" => null,
            EvidentiaAttachmentPlacement.Rendering => rendering,
            EvidentiaAttachmentPlacement.Dependent => index > 0
                && placedBySource.TryGetValue(words[index - 1].Token.Id, out var governing)
                && Class(words[index - 1]) is "noun" or "propn" or "adj" or "num"
                    ? Dependent(governing.Target, rendering, verse)
                    : null,
            EvidentiaAttachmentPlacement.OwnKindBefore => OwnKindBefore(attachment, word, rendering, verse, taken),
            EvidentiaAttachmentPlacement.PhraseStart => PhraseStart(words, index, verse, placedBySource, taken),
            EvidentiaAttachmentPlacement.AgreeingRendering => afterVerb
                ? PrepositionAfter(word, rendering, verse, taken)
                : Agreeing(attachment, word, rendering, verse, taken),
            EvidentiaAttachmentPlacement.Infinitive => Infinitive(rendering, verse, taken),
            EvidentiaAttachmentPlacement.BeBeside => BeBeside(rendering, verse, taken),
            EvidentiaAttachmentPlacement.ConjunctionBefore => ConjunctionBefore(rendering, verse, taken),
            EvidentiaAttachmentPlacement.ClauseConjunction => ClauseConjunction(rendering, verse, taken),
            EvidentiaAttachmentPlacement.LamedBefore =>
                PrepositionBefore(rendering, verse, taken) is { Token.StrongNumber: Lamed } lamed ? lamed : null,
            EvidentiaAttachmentPlacement.BeVerb =>
                rendering.Token.StrongNumber is { } be && BeVerbs.Contains(be) ? rendering : null,
            EvidentiaAttachmentPlacement.InfinitiveAbsolute => InfinitiveAbsoluteBeside(rendering, verse, taken),
            _ => null,
        };
    }

    /// <summary>
    /// The word a form of <em>be</em> links, whose rendering its verb stands beside: the subject of
    /// <em>there was light</em>, which the parse makes the verb's own, and the predicate of <em>it was
    /// good</em>, which the parse makes the verb's head.
    /// </summary>
    private static EvidentiaAnalysis? CopulaAnchor(IReadOnlyList<EvidentiaAnalysis> words, EvidentiaAnalysis be)
    {
        var dependents = words.Where(other => other.Token.SyntacticHead == be.Token.Id).ToList();
        if (dependents.Any(other => other.Token.Relation == ExpletiveRelation))
        {
            var subjects = dependents.Where(other => other.Token.Relation == SubjectRelation).ToList();
            return subjects.Count == 1 ? subjects[0] : null;
        }

        return be.Token.Relation == CopulaRelation ? Syntactic(words, be.Token.SyntacticHead) : null;
    }

    private static EvidentiaAnalysis? Syntactic(IReadOnlyList<EvidentiaAnalysis> words, long? id) =>
        id is { } head ? words.FirstOrDefault(other => other.Token.Id == head) : null;

    /// <summary>
    /// The original's own <em>be</em>, free, written directly before or after the rendering: וַיְהִי עֶרֶב.
    /// Further away it is as often another clause's verb.
    /// </summary>
    private static EvidentiaAnalysis? BeBeside(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, rendering);
        var beside = new[] { at - 1, at + 1 }
            .Where(index => at >= 0 && index >= 0 && index < verse.Count)
            .Select(index => verse[index])
            .Where(word => word.Token.StrongNumber is { } strong && BeVerbs.Contains(strong) && !taken.Contains(word.Token.Id))
            .ToList();
        return beside.Count == 1 ? beside[0] : null;
    }

    /// <summary>
    /// Where <em>to</em> goes once its verb is placed on an infinitive: on the ל written before it, or on
    /// the infinitive itself where the original writes none, as Greek never does. A verb rendered by any
    /// other form leaves it unplaced, since <em>to</em> is then often a ἵνα the parse does not see.
    /// </summary>
    private static EvidentiaAnalysis? Infinitive(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        if (!IsInfinitive(rendering))
        {
            return null;
        }

        var at = IndexOf(verse, rendering);
        return at > 0 && Class(verse[at - 1]) == "adp" && !taken.Contains(verse[at - 1].Token.Id)
            && verse[at - 1].Token.StrongNumber != ObjectMarker
            ? verse[at - 1]
            : rendering;
    }

    /// <summary>
    /// The conjunction written directly before the rendering of the clause's verb - כִּי, אֲשֶׁר, ὅτι,
    /// εἰ - when it is free and is not the original's <em>and</em>. One word further back it is as often
    /// the conjunction of another clause.
    /// </summary>
    private static EvidentiaAnalysis? ConjunctionBefore(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, rendering);
        return at > 0 && Class(verse[at - 1]) == "conj" && !taken.Contains(verse[at - 1].Token.Id)
            && !(verse[at - 1].Token.StrongNumber is { } strong && Coordinators.Contains(strong))
            ? verse[at - 1]
            : null;
    }

    /// <summary>
    /// The conjunction that opens the clause of the rendering, if free: in Hebrew the one written before
    /// it past a preposition or article, in Greek one of its clause conjunctions standing before it with
    /// no other verb between.
    /// </summary>
    private static EvidentiaAnalysis? ClauseConjunction(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, rendering);
        if (rendering.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
        {
            for (var before = at - 1; before >= 0 && Class(verse[before]) is "det" or "adp" or "conj"; before--)
            {
                if (Class(verse[before]) == "conj")
                {
                    return taken.Contains(verse[before].Token.Id) ? null : verse[before];
                }
            }

            return null;
        }

        for (var before = at - 1; before >= 0 && at - before <= ClauseConjunctionReach && Class(verse[before]) != "verb"; before--)
        {
            if (verse[before].Token.StrongNumber is { } strong && GreekClauseConjunctions.Contains(strong))
            {
                return taken.Contains(verse[before].Token.Id) ? null : verse[before];
            }
        }

        return null;
    }

    /// <summary>The free infinitive absolute of the rendering's own lexeme, written just before or after it: מוֹת תָּמוּת.</summary>
    private static EvidentiaAnalysis? InfinitiveAbsoluteBeside(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, rendering);
        return new[] { at - 1, at + 1, at - 2 }
            .Where(index => at >= 0 && index >= 0 && index < verse.Count)
            .Select(index => verse[index])
            .FirstOrDefault(word => !taken.Contains(word.Token.Id) && word.Token.StrongNumber == rendering.Token.StrongNumber
                && string.Equals(Feature(word, "tense"), InfinitiveAbsoluteTense, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// The original's own auxiliary, where it writes a tense in two words: then the auxiliary of <em>war
    /// gelegt</em> or <em>había de venir</em> is that word and not the verb. Greek's εἰμί beside a
    /// participle or an adjective (<em>ἦν βεβλημένος</em>, <em>κλητοῖς οὖσιν</em>) and its μέλλω before an
    /// infinitive (<em>ὁ μέλλων ἔρχεσθαι</em>); Hebrew's היה beside a participle (<em>וָאֱהִי נָגוּעַ</em>).
    /// After the word only directly: one further on it opens the next clause, <em>ἐγερθείς, ὅς ἐστιν</em>.
    /// </summary>
    private static EvidentiaAnalysis? OwnAuxiliary(EvidentiaAnalysis rendering, IReadOnlyList<EvidentiaAnalysis> verse)
    {
        var at = IndexOf(verse, rendering);
        var greek = rendering.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase);
        var before = verse.Skip(Math.Max(0, at - OwnAuxiliaryReach)).Take(Math.Min(at, OwnAuxiliaryReach)).Reverse();
        if (greek && IsInfinitive(rendering))
        {
            return before.FirstOrDefault(word => word.Token.StrongNumber == GreekAboutTo);
        }

        if (!(greek ? IsParticiple(rendering) || Class(rendering) == "adj" : IsHebrewParticiple(rendering)))
        {
            return null;
        }

        var be = greek ? GreekBe : HebrewBe;
        return before.Concat(verse.Skip(at + 1).Take(1)).FirstOrDefault(word => word.Token.StrongNumber == be);
    }

    private static bool IsFinite(EvidentiaAnalysis word) => Class(word) == "verb" && Feature(word, "person") is not null;

    private static bool IsParticiple(EvidentiaAnalysis word) =>
        string.Equals(Feature(word, "mood"), "participle", StringComparison.OrdinalIgnoreCase);

    private static bool IsHebrewParticiple(EvidentiaAnalysis word) =>
        Feature(word, "tense") is { } tense && HebrewParticiples.Contains(tense);

    private static bool IsInfinitive(EvidentiaAnalysis word) =>
        EvidentiaPersonAgreement.IsInfinitive(word)
        || string.Equals(Feature(word, "mood"), "infinitive", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Where <em>of</em> goes once the noun before it and the noun after it are placed: on the governed
    /// rendering, when the governing one is a construct standing before it with at most an article between.
    /// </summary>
    private static EvidentiaAnalysis? Dependent(
        EvidentiaAnalysis governing,
        EvidentiaAnalysis governed,
        IReadOnlyList<EvidentiaAnalysis> verse)
    {
        var from = IndexOf(verse, governing);
        var to = IndexOf(verse, governed);
        return from >= 0 && to > from
            && verse.Skip(from + 1).Take(to - from - 1).All(between => Class(between) == "det")
            && string.Equals(Feature(governing, "state"), ConstructState, StringComparison.OrdinalIgnoreCase)
                ? governed
                : null;
    }

    /// <summary>
    /// The rendering, if it names the pronoun's person: a subject in the verb's own ending - a
    /// participle names none - or in the suffix of a Hebrew infinitive; a possessive or an object in
    /// the suffix.
    /// </summary>
    private static bool Attaches(string language) =>
        language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase)
        || language.Equals(GermanLanguage, StringComparison.OrdinalIgnoreCase)
        || language.Equals(SpanishLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whose parse said the word belongs to that verb; it goes into the note of every claim written from it.</summary>
    private static string ParseCredit(EvidentiaAnalysis word) =>
        word.Token.Language.Equals(GermanLanguage, StringComparison.OrdinalIgnoreCase) ? GermanParse
        : word.Token.Language.Equals(SpanishLanguage, StringComparison.OrdinalIgnoreCase) ? SpanishParse
        : string.Empty;

    private const string GermanParse =
        ", by the parse of UDPipe's German-HDT model (Straka and Straková, ÚFAL, CC BY-NC-SA 4.0)";

    private const string SpanishParse =
        ", by the parse of UDPipe's Spanish-AnCora model (Straka and Straková, ÚFAL, CC BY-NC-SA 4.0)";

    private static bool IsEnglish(EvidentiaAnalysis word) =>
        word.Token.Language.Equals(EnglishLanguage, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A German or Spanish word that writes the inflection of its verb: the subject pronoun of <em>er
    /// sprach</em> and <em>él dijo</em>, which a Hebrew or Greek verb carries in its ending, and the
    /// auxiliary of <em>wird sagen</em> or <em>ha dicho</em>, which it carries in its tense. The verb is
    /// the one the parse makes it depend on, since German puts the verb second or last and the pronoun
    /// after it as often as before. <em>es</em> is left alone, as <em>it</em> is: it is as often the
    /// empty subject of <em>es geschah</em>; so is a pronoun after a preposition, which the parse takes
    /// for the subject of <em>envió á él</em>. An auxiliary belongs to a participle or an infinitive; one
    /// the parse hangs on a finite verb is the verb of another clause, or the <em>he</em> of <em>he aquí</em>.
    /// </summary>
    private static (EvidentiaAttachment? Attachment, EvidentiaAnalysis? Head) ClassifyInflection(
        IReadOnlyList<EvidentiaAnalysis> words,
        EvidentiaAnalysis word)
    {
        if (Syntactic(words, word.Token.SyntacticHead) is not { } verb || Class(verb) != "verb")
        {
            return (null, null);
        }

        var partOfSpeech = Class(word);
        var at = IndexOf(words, word);
        if (partOfSpeech == "pron" && word.Token.Relation is SubjectRelation or PassiveSubjectRelation
            && !(at > 0 && Class(words[at - 1]) == "adp" && !Punctuated(words[at - 1]))
            && Feature(word, "PronType") == "Prs" && Feature(word, "Poss") is null && Feature(word, "Reflex") is null
            && Feature(word, "Gender") != NeuterGender && EvidentiaPersonAgreement.Person(word) is not null)
        {
            return (EvidentiaAttachment.SubjectPronoun, verb);
        }

        return partOfSpeech == "aux" && word.Token.Relation is { } relation && AuxiliaryRelations.Contains(relation)
            && word.Token.Lemma is { } lemma && InflectionAuxiliaries.TryGetValue(lemma, out var forms)
            && Feature(verb, "VerbForm") is { } form && forms.Contains(form)
                ? (EvidentiaAttachment.AuxiliaryVerb, verb)
                : (null, null);
    }

    /// <summary>
    /// A German or Spanish subject pronoun goes on the verb whose ending names its person, and an
    /// auxiliary on the verb itself, in Hebrew and in Greek alike, since the verb the main verb was
    /// placed on is the one whose tense the auxiliary writes; <see cref="OwnAuxiliary"/> is the exception.
    /// Where the main verb was placed on a word that is no verb, the auxiliary is left alone: the parse
    /// reads <em>era sábado</em> and <em>no había agua</em> as a tense of a participle.
    /// </summary>
    private static EvidentiaAttachmentPlacement InflectionPlacement(EvidentiaAttachment attachment, string witnessLanguage) =>
        !witnessLanguage.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
        && !witnessLanguage.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
            ? EvidentiaAttachmentPlacement.None
            : attachment switch
            {
                EvidentiaAttachment.SubjectPronoun => EvidentiaAttachmentPlacement.AgreeingRendering,
                EvidentiaAttachment.AuxiliaryVerb => EvidentiaAttachmentPlacement.Rendering,
                _ => EvidentiaAttachmentPlacement.None,
            };

    private static EvidentiaAnalysis? Agreeing(
        EvidentiaAttachment attachment,
        EvidentiaAnalysis pronoun,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken) =>
        attachment switch
        {
            EvidentiaAttachment.SubjectPronoun when Class(rendering) == "verb"
                && (EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: false)
                    || EvidentiaPersonAgreement.IsInfinitive(rendering) && EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: true))
                && !WritesTheSubjectApart(pronoun, rendering, verse, taken) => rendering,
            EvidentiaAttachment.PossessivePronoun or EvidentiaAttachment.ObjectPronoun
                when EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: true) => rendering,
            _ => null,
        };

    /// <summary>
    /// A pronoun of the same person standing by the verb, which is then where the subject is written.
    /// A German or Spanish pronoun stands as far from its verb as the clause is long - <em>Tú, con todo,
    /// por tus muchas misericordias no los abandonaste</em> - so there a pronoun of the original that no
    /// word renders yet counts anywhere in the verse, and so does the Greek article standing for the
    /// person before the verb, with no other finite verb between them: <em>οἱ δὲ εἶπαν</em> is <em>y
    /// ellos dijeron</em>, and <em>οἱ δὲ εὐθέως ἀφέντες τὰ δίκτυα ἠκολούθησαν</em> is <em>ellos siguieron</em>.
    /// </summary>
    private static bool WritesTheSubjectApart(
        EvidentiaAnalysis pronoun,
        EvidentiaAnalysis verb,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken) =>
        verse.Any(word => word.Token.Id != verb.Token.Id
            && (Math.Abs(word.Token.Position - verb.Token.Position) <= SubjectPronounReach
                || !IsEnglish(pronoun) && !taken.Contains(word.Token.Id))
            && Class(word) == "pron"
            && EvidentiaPersonAgreement.CouldBeTheSubject(pronoun, word))
        || !IsEnglish(pronoun) && EvidentiaAuxiliaryWords.Mark(verse).Any(word =>
            word.Role == EvidentiaAuxiliaryRole.PronominalArticle
            && word.Token.Position < verb.Token.Position
            && !verse.Any(between => between.Token.Position > word.Token.Position
                && between.Token.Position < verb.Token.Position && IsFinite(between)));

    private static EvidentiaAnalysis? PrepositionAfter(
        EvidentiaAnalysis pronoun,
        EvidentiaAnalysis verb,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, verb);
        return at < 0
            ? null
            : verse.Skip(at + 1).Take(ObjectPrepositionReach).FirstOrDefault(word =>
                Class(word) == "adp" && !taken.Contains(word.Token.Id)
                && EvidentiaPersonAgreement.Agrees(pronoun, word, suffix: true));
    }

    internal static int IndexOf(IReadOnlyList<EvidentiaAnalysis> words, EvidentiaAnalysis word)
    {
        for (var index = 0; index < words.Count; index++)
        {
            if (words[index].Token.Id == word.Token.Id)
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>
    /// The word of the attachment's kind among the grammatical words written directly before the
    /// rendering. BHSA writes a conjunction, a preposition and an article as words of their own, in
    /// that order back from the word they belong to: וּ בַ הַ מַּיִם.
    /// </summary>
    private static EvidentiaAnalysis? OwnKindBefore(
        EvidentiaAttachment attachment,
        EvidentiaAnalysis word,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        if (attachment == EvidentiaAttachment.Preposition)
        {
            return Class(rendering) == "adp" ? rendering : PrepositionBefore(rendering, verse, taken);
        }

        if (attachment == EvidentiaAttachment.Article)
        {
            return rendering.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
                ? HebrewArticleBefore(word, rendering, verse, taken)
                : ArticleBefore(word, rendering, verse, taken);
        }

        var wanted = attachment == EvidentiaAttachment.Conjunction ? "conj" : null;
        var before = IndexOf(verse, rendering);
        for (before--; wanted is not null && before >= 0 && Class(verse[before]) is "det" or "adp" or "conj"; before--)
        {
            if (Class(verse[before]) == wanted && IsTheWitnessAnd(verse[before]))
            {
                return verse[before];
            }
        }

        return null;
    }

    /// <summary>
    /// The Greek article written before the rendering, past an adjective it also governs: <em>the</em>
    /// of <em>the other side</em> on τὸ of τὸ πέραν. Greek has no indefinite article, so only
    /// <em>the</em> is placed.
    /// </summary>
    private static EvidentiaAnalysis? ArticleBefore(
        EvidentiaAnalysis article,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        if (!article.Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        for (var before = IndexOf(verse, rendering) - 1; before >= 0; before--)
        {
            if (Class(verse[before]) == "det")
            {
                return taken.Contains(verse[before].Token.Id) ? null : verse[before];
            }

            if (Class(verse[before]) != "adj")
            {
                return null;
            }
        }

        return null;
    }

    /// <summary>
    /// The article BHSA writes as the prefix of the rendering, spelt out or swallowed by a preposition
    /// (לָ of לָאוֹר). A rendering without one leaves <em>the</em> to be said supplied.
    /// </summary>
    private static EvidentiaAnalysis? HebrewArticleBefore(
        EvidentiaAnalysis article,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        var at = IndexOf(verse, rendering);
        return article.Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase)
            && at > 0 && verse[at - 1].Token.StrongNumber == HebrewArticle && !taken.Contains(verse[at - 1].Token.Id)
                ? verse[at - 1]
                : null;
    }

    /// <summary>
    /// The preposition before the first word the phrase's renderings take, where they stand together:
    /// <em>in his vineyard</em> is ἐν τῷ ἀμπελῶνι αὐτοῦ, whose possessive comes last, and <em>to all the
    /// kings</em> is לְ כָל מַלְכֵי, whose quantifier stands between the preposition and the noun.
    /// </summary>
    private static EvidentiaAnalysis? PhraseStart(
        IReadOnlyList<EvidentiaAnalysis> words,
        int index,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlySet<long> taken)
    {
        if (Syntactic(words, words[index].Token.SyntacticHead) is not { } governed || IndexOf(words, governed) is var head && head <= index)
        {
            return null;
        }

        // Hebrew writes a possessive on its noun, so a phrase the preposition's own rule missed is as often
        // one it should not reach; only a quantifier standing between them (לְ כָל מַלְכֵי) held.
        if (verse.Count > 0 && verse[0].Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase)
            && !Quantifiers.Contains(words[index + 1].Token.Surface))
        {
            return null;
        }

        var at = Enumerable.Range(index + 1, head - index)
            .Select(phrase => placedBySource.TryGetValue(words[phrase].Token.Id, out var placed) ? IndexOf(verse, placed.Target) : -1)
            .Where(position => position >= 0)
            .ToList();
        return at.Count > 0 && at.Max() - at.Min() <= head - index
            ? PrepositionBefore(verse[at.Min()], verse, taken)
            : null;
    }

    /// <summary>The nearest preposition written before the rendering, past an article: מִן הָ אֲדָמָה.</summary>
    private static EvidentiaAnalysis? PrepositionBefore(
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken)
    {
        for (var before = IndexOf(verse, rendering) - 1; before >= 0 && Class(verse[before]) is "det" or "adp" or "conj"; before--)
        {
            if (Class(verse[before]) == "adp")
            {
                return taken.Contains(verse[before].Token.Id) || verse[before].Token.StrongNumber == ObjectMarker
                    ? null
                    : verse[before];
            }
        }

        return null;
    }

    private static bool IsTheWitnessAnd(EvidentiaAnalysis word) =>
        !word.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
        || string.Equals(word.Token.Lemma, GreekAnd, StringComparison.Ordinal);

    private static (EvidentiaAttachment?, EvidentiaAnalysis?) Found(EvidentiaAttachment attachment, EvidentiaAnalysis? head) =>
        head is null ? (null, null) : (attachment, head);

    /// <summary>
    /// The nearest following word of one of the classes, within reach, not across punctuation or a
    /// negation: <em>must not eat</em> is annotated with its negation, which an original writes apart.
    /// </summary>
    internal static EvidentiaAnalysis? Forward(IReadOnlyList<EvidentiaAnalysis> words, int index, params string[] classes)
    {
        for (var next = index + 1; next < words.Count && next - index <= HeadReach; next++)
        {
            if (Punctuated(words[next - 1]) || Negations.Contains(words[next].Token.Surface))
            {
                return null;
            }

            if (classes.Contains(Class(words[next])))
            {
                return words[next];
            }
        }

        return null;
    }

    /// <summary>The nearest earlier word of one of the classes, within reach, not across punctuation or a negation.</summary>
    private static EvidentiaAnalysis? Backward(IReadOnlyList<EvidentiaAnalysis> words, int index, params string[] classes)
    {
        for (var back = index - 1; back >= 0 && index - back <= HeadReach; back--)
        {
            if (Punctuated(words[back]) || Negations.Contains(words[back].Token.Surface))
            {
                return null;
            }

            if (classes.Contains(Class(words[back])))
            {
                return words[back];
            }
        }

        return null;
    }

    private static bool Punctuated(EvidentiaAnalysis word) => word.Token.Trailer.Any(char.IsPunctuation);

    private static string? Feature(EvidentiaAnalysis word, string name) =>
        word.Token.Morphology?.FirstOrDefault(pair => pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase)).Value;

    internal static string? Class(EvidentiaAnalysis word) =>
        EvidentiaMorphologyLabels.PartOfSpeech(word.PartOfSpeech ?? word.Token.PartOfSpeech, word.Token.Language);
}
