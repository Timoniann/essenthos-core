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

    /// <summary>
    /// <em>of</em> of <em>the face of the deep</em>, which Hebrew writes as a construct chain: on the governed noun,
    /// or, where <em>of</em> goes with the construct, on the construct the word before it was placed on.
    /// </summary>
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

    /// <summary>
    /// <em>the</em> of <em>the word of the LORD</em>: no article is written, and the noun is definite by its grammar,
    /// as the head of a construct chain or a name, so <em>the</em> goes with the noun.
    /// </summary>
    DefiniteNoun,

    /// <summary>
    /// <em>of</em> where Greek writes no word for it: on the genitive, <em>the mind of the flesh</em> on τῆς of
    /// τῆς σαρκός.
    /// </summary>
    OfTheGoverned,

    /// <summary>
    /// <em>of</em> where Hebrew writes no word for it and the word before it was not the one placed on the
    /// construct: <em>the cry of the afflicted</em> on צַעֲקַת, the construct עֲנִיִּים depends on.
    /// </summary>
    OfTheConstruct,

    /// <summary>
    /// <em>is</em> of a clause BHSA types as verbless: on the head of its predicate complement, <em>Life is good</em>
    /// on the word of <em>good</em>, and on the demonstrative where the subject is one, <em>This is the book</em> on זֶה.
    /// </summary>
    CopulaOnThePredicate,

    /// <summary>
    /// <em>was</em> of <em>the earth was corrupt</em>, where the original says the predicate with a finite verb and
    /// writes no <em>be</em> of its own: on that verb, תִּשָּׁחֵת, as the tense of the word it goes with.
    /// </summary>
    BeOnTheVerb,

    /// <summary>
    /// <em>will</em> of <em>will die</em> on the Greek future ἀποθανεῖται, <em>was</em> of <em>was baptized</em> on the
    /// passive ἐβαπτίσθη, <em>might</em> of <em>might believe</em> on the subjunctive: an English auxiliary of a Greek
    /// verb whose own form carries that tense, mood or voice; on the εἰμί or μέλλω where the original writes one.
    /// </summary>
    GreekAuxiliary,

    /// <summary>
    /// <em>did</em> of <em>did not see</em>, <em>will</em> of <em>will not go</em>: the auxiliary on the Hebrew verb whose
    /// form carries its tense, across the negation the original writes as a word of its own.
    /// </summary>
    AuxiliaryAcrossNegation,

    /// <summary><em>Let us</em> of <em>Let us make</em>: on the cohortative נַעֲשֶׂה, or the jussive, the Greek hortatory subjunctive or imperative.</summary>
    LetOfTheMood,

    /// <summary><em>to</em> of <em>to God</em>, which Greek writes as the dative τῷ Θεῷ: on the noun, or on its article where English writes <em>the</em> or a possessive.</summary>
    DativeTo,

    /// <summary>
    /// <em>who</em> and <em>the One</em> of <em>the One who comes</em>, which Greek writes as the article of the participle,
    /// ὁ ἐρχόμενος, and Hebrew as the participle itself.
    /// </summary>
    RelativeOfTheParticiple,
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

    /// <summary>
    /// On the rendering, where the original writes no article before it and makes it definite by its grammar: the head of
    /// a construct chain in which no article is written, or a name.
    /// </summary>
    DefiniteByGrammar,
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
/// joins; an auxiliary goes on the verb, which carries tense and mood in itself, and on nothing else:
/// where its verb was placed on a noun (<em>had followed</em> on אַחֲרֵי) there is no tense for it to
/// write, and where the verb's ending names another person than the subject the parse gives the English
/// verb (<em>May the LORD make</em> on the imperative עֲשֵׂה, <em>he had become</em> on וַיִּהְיוּ) the
/// verb is another clause's. Nor on an infinitive: a verb placed on one was the wrong verb one time in
/// six on the passages measured, the tense being written in the finite verb beside it (<em>shall we
/// again break</em>, נָשׁוּב לְהָפֵר).</para>
///
/// <para>A personal pronoun goes where the original writes the person: a subject on the verb whose
/// ending names it, a possessive on the Hebrew noun whose suffix does, an object on the suffix of the
/// verb or preposition - the object of a verb on a verb only, since the suffix of a noun its verb was
/// placed on (<em>named him</em> on שְׁמוֹ) is a possessive. The morphology decides, not the habit: a pronoun whose head was placed on a
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
internal static partial class EvidentiaAttachedWords
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

    private const string NominativeCase = "Nom";

    private const string PersonalPronounType = "Prs";

    private const string ThirdPerson = "3";

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

    private const string GreekGenitive = "genitive";

    /// <summary>The words after <em>of</em> the keys put it on themselves: <em>of his heart</em>, <em>of this man</em>.</summary>
    private static readonly HashSet<string> OwnersAfterOf = new(StringComparer.OrdinalIgnoreCase)
    {
        "his", "her", "my", "your", "our", "their", "its", "whose", "this", "that", "these", "those",
    };

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

    /// <summary>
    /// The clauses BHSA types as having no finite verb: a nominal clause, an adjective clause, and a participle
    /// clause, whose participle is its predicate complement (<em>Blessed be the LORD</em>, בָּרוּךְ יְהוָה).
    /// </summary>
    private static readonly HashSet<string> VerblessClauses = new(StringComparer.Ordinal) { "NmCl", "AjCl", "Ptcp" };

    /// <summary>BHSA's predicate complement, and the same with the subject in its suffix.</summary>
    private static readonly HashSet<string> PredicateComplements = new(StringComparer.Ordinal) { "PreC", "PrcS" };

    /// <summary>The phrases by which a clause writes its own <em>is</em>: יֵשׁ and אַיִן, with or without a suffix.</summary>
    private static readonly HashSet<string> WrittenBeFunctions = new(StringComparer.Ordinal) { "Exst", "ExsS", "NCop", "NCoS" };

    /// <summary>הוּא, הִיא, הֵם, הֵמָּה and הֵנָּה, which stand for <em>is</em> where no word of the translation takes them as the subject.</summary>
    private static readonly HashSet<string> HebrewCopulaPronouns = new(StringComparer.Ordinal) { "H1931", "H1932", "H1992", "H1993", "H2007" };

    private const string SubjectFunction = "Subj";

    /// <summary>BHSA's demonstrative pronoun: זֶה, זֹאת, אֵלֶּה.</summary>
    private const string HebrewDemonstrative = "prde";

    /// <summary>BHSA's interrogative pronoun and interrogative: מִי, מָה, אֵי.</summary>
    private static readonly HashSet<string> HebrewInterrogatives = new(StringComparer.Ordinal) { "prin", "inrg" };

    /// <summary>The tenses of a Hebrew verb that is finite, which a verbless clause never has.</summary>
    private static readonly HashSet<string> HebrewFiniteTenses = new(StringComparer.OrdinalIgnoreCase) { "perf", "impf", "wayq", "impv" };

    /// <summary>כֹּל, which stands before a demonstrative subject without making it any less one: <em>all these</em>.</summary>
    private const string HebrewAll = "H3605";

    /// <summary>The question words and <em>here</em> that, opening a sentence, are its predicate: <em>What is your country</em>.</summary>
    private static readonly HashSet<string> FrontedQuestions = new(StringComparer.OrdinalIgnoreCase) { "what", "where", "here" };

    private static readonly char[] OpeningQuotes = ['“', '‘', '"', '\''];

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

    /// <param name="definiteNouns">
    /// Whether <em>the</em> with no article of its own goes with a Hebrew noun its grammar makes definite
    /// (<see cref="EvidentiaAttachment.DefiniteNoun"/>) rather than being left to be said supplied.
    /// </param>
    /// <param name="functionWords">
    /// Whether a Hebrew <em>of</em> goes with the construct rather than the governed noun, and an <em>of</em> the
    /// original writes no word for is placed once every other rule has had its turn: with the construct in
    /// Hebrew (<see cref="EvidentiaAttachment.OfTheConstruct"/>), on the genitive in Greek
    /// (<see cref="EvidentiaAttachment.OfTheGoverned"/>); and a Hebrew <em>is</em> in a clause the original's
    /// syntax types as verbless (<see cref="EvidentiaAttachment.CopulaOnThePredicate"/>), read from
    /// <paramref name="clauses"/>, without which it places none; and the English words that say what one word of the
    /// original carries in its form go with that word (<see cref="AttachInflection"/>).
    /// </param>
    public static IReadOnlyList<EvidentiaProposal> Resolve(
        IReadOnlyList<EvidentiaAnalysis> source,
        IReadOnlyList<EvidentiaAnalysis> target,
        IReadOnlyList<EvidentiaProposal> placed,
        bool definiteNouns = false,
        bool functionWords = false,
        EvidentiaClauses? clauses = null)
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
                placing |= Attach(words, placedBySource, targetsByVerse, taken, proposals, definiteNouns, functionWords);
            }
        }

        if (functionWords)
        {
            var renderedBy = placed.ToLookup(proposal => proposal.Target.Token.Id);
            foreach (var words in verses)
            {
                AttachOf(words, placedBySource, targetsByVerse, renderedBy, proposals);
                if (clauses is not null)
                {
                    AttachIs(words, placedBySource, targetsByVerse, taken, clauses, proposals);
                }

                AttachInflection(words, placedBySource, targetsByVerse, taken, renderedBy, proposals);
            }
        }

        foreach (var words in verses)
        {
            AttachToPrefixes(words, placedBySource, targetsByVerse, taken, proposals);
        }

        return proposals;
    }

    /// <summary>
    /// An <em>of</em> no rule placed, where the original writes no word for it, read from the original of the first
    /// word after it, past an article. In Hebrew that word is the noun of its phrase, past the modifiers English
    /// writes first (<em>of Noah's sons</em>, <em>of the barley harvest</em>), and only where a noun in the construct
    /// state stands directly before its word, past its article, <em>of</em> goes with that construct: <em>the cry
    /// of the afflicted</em> on צַעֲקַת, and in a longer chain each <em>of</em> with the construct before its own noun.
    /// In Greek only where that word is in the genitive, and on the article written before it when English writes
    /// <em>the</em> too (<em>the mind of the flesh</em> on τῆς). Nothing where a preposition is written before it
    /// (מִן, ἐκ: the original's own word for <em>of</em>), and nothing where a demonstrative, or in Greek a
    /// possessive, follows <em>of</em>: the keys put it on that word.
    ///
    /// <para>It goes on a word that is already held, as <see cref="EvidentiaAttachment.Genitive"/> does, and takes
    /// nothing from any other rule. A Hebrew <em>of</em> belongs to the word placed on its construct where one
    /// was, and otherwise to the noun after it.</para>
    /// </summary>
    private static void AttachOf(
        IReadOnlyList<EvidentiaAnalysis> words,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        ILookup<long, EvidentiaProposal> renderedBy,
        List<EvidentiaProposal> proposals)
    {
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            if (!word.Token.Surface.Equals(Of, StringComparison.OrdinalIgnoreCase)
                || word.Token.Relation?.StartsWith("case", StringComparison.Ordinal) != true
                || placedBySource.ContainsKey(word.Token.Id))
            {
                continue;
            }

            var next = index + 1;
            while (next < words.Count && Articles.Contains(words[next].Token.Surface))
            {
                next++;
            }

            if (next < words.Count && NounOfThePhrase(words, next) is var noun && noun != next
                && placedBySource.TryGetValue(words[noun].Token.Id, out var nounPlaced)
                && nounPlaced.Target.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
            {
                next = noun;
            }

            if (next >= words.Count
                || OwnersAfterOf.Contains(words[next].Token.Surface)
                || !placedBySource.TryGetValue(words[next].Token.Id, out var head)
                || head.Kind == EvidentiaProposalKind.AttachedWord
                || !targetsByVerse.TryGetValue(head.Target.Token.Address, out var verse))
            {
                continue;
            }

            var rendering = head.Target;
            var at = verse.FindIndex(analysis => analysis.Token.Id == rendering.Token.Id);
            var before = at > 0 ? verse[at - 1] : null;
            EvidentiaAnalysis? placement = null;
            var attachment = EvidentiaAttachment.OfTheGoverned;
            if (rendering.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
            {
                var back = at - 1;
                while (back >= 0 && verse[back].Token.StrongNumber == HebrewArticle)
                {
                    back--;
                }

                if (back >= 0 && Class(verse[back]) != "adp" && Feature(verse[back], "state") == ConstructState)
                {
                    placement = verse[back];
                    attachment = EvidentiaAttachment.OfTheConstruct;
                    head = renderedBy[placement.Token.Id].FirstOrDefault(proposal => proposal.Kind != EvidentiaProposalKind.AttachedWord
                        && proposal.Source.Token.Address.Equals(word.Token.Address)) ?? head;
                }
            }
            else if (rendering.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase)
                     && Feature(rendering, "case") == GreekGenitive)
            {
                var article = before is not null && Class(before) == "det" && Feature(before, "case") == GreekGenitive ? before : null;
                var preceding = article is not null ? (at > 1 ? verse[at - 2] : null) : before;
                if (preceding is null || Class(preceding) != "adp")
                {
                    placement = article is not null && index + 1 < next
                        && words[index + 1].Token.Surface.Equals(DefiniteArticle, StringComparison.OrdinalIgnoreCase)
                        ? article
                        : rendering;
                }
            }

            if (placement is null)
            {
                continue;
            }

            var proposal = new EvidentiaProposal(
                word,
                placement,
                EvidentiaProposalKind.AttachedWord,
                Math.Max(0, head.Confidence - ConfidenceBelowHead),
                new EvidentiaDecisionTrace(
                    "attached",
                    $"{attachment} of '{head.Source.Token.Surface}'{ParseCredit(word)}",
                    head.Trace?.Evidence ?? []),
                head);
            proposals.Add(proposal);
            placedBySource[word.Token.Id] = proposal;
        }
    }

    /// <summary>
    /// A form of <em>be</em> no rule placed, decided by the original's own syntax rather than by the English parse.
    /// Its English predicate - the word the parse hangs it on, or a predicate English writes before it - must have
    /// been placed in one clause of the original. Where BHSA types that clause as verbless
    /// (<see cref="VerblessClauses"/>), the predicate complement takes it; a subject placed alone does not say which clause English's predicate is in.
    /// The subject must stand in the same clause, or in another only where this clause
    /// writes none (<em>God saw that the light was good</em>: הָאוֹר is the object of וַיַּרְא, כִּי טוֹב has no
    /// subject). Then <em>is</em> goes on the head of the clause's predicate complement (<see cref="PhraseHead"/>):
    /// the noun of a prepositional one, the interrogative of a question (<em>who is this Philistine</em> on מִי),
    /// the adjective English puts first (<em>Great is the LORD</em> on גָּדוֹל). Where the
    /// subject is a demonstrative and the predicate no interrogative, on the demonstrative (<em>These are the
    /// sons</em> on אֵלֶּה), as unfoldingWord aligns it.
    ///
    /// <para>Where the clause is verbal and English's predicate - an adjective, a participle or a noun after
    /// <em>be</em> - was placed on its finite verb, <em>be</em> goes on that verb as the tense of the word it goes
    /// with (<see cref="EvidentiaAttachment.BeOnTheVerb"/>: <em>the earth was corrupt</em> on תִּשָּׁחֵת, <em>Be
    /// fruitful</em> on פְּרוּ).</para>
    ///
    /// <para>Nothing where the English clause reaches into two of the original's clauses or into none, where the
    /// clause writes היה, יֵשׁ or אַיִן, or a הוּא no word of the translation took as its subject: that word is the
    /// <em>is</em>. Nothing in a verbal clause whose verb English's predicate was not placed on. Greek is left
    /// alone.</para>
    /// </summary>
    private static void AttachIs(
        IReadOnlyList<EvidentiaAnalysis> words,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        IReadOnlySet<long> taken,
        EvidentiaClauses clauses,
        List<EvidentiaProposal> proposals)
    {
        for (var index = 0; index < words.Count; index++)
        {
            var word = words[index];
            if (!IsEnglish(word) || word.Lemma != Be || word.Token.Relation != CopulaRelation
                || placedBySource.ContainsKey(word.Token.Id)
                || Syntactic(words, word.Token.SyntacticHead) is not { } parsed)
            {
                continue;
            }

            var subject = words.FirstOrDefault(other => other.Token.SyntacticHead == parsed.Token.Id
                && other.Token.Relation?.StartsWith(SubjectRelation, StringComparison.Ordinal) == true);
            var predicates = Placed([FrontedPredicate(words, index), parsed], placedBySource);
            var subjectPlaced = Placed([subject], placedBySource).FirstOrDefault();
            List<EvidentiaProposal> anchors = [.. predicates, .. subjectPlaced is null ? [] : new[] { subjectPlaced }];
            var places = predicates.Select(anchor => clauses.Of(anchor.Target)).ToList();
            if (predicates.Count == 0 || places.Any(place => place is null)
                || places.Select(place => place!.Value.Clause).Distinct().Count() != 1
                || predicates.Select(anchor => anchor.Target.Token.Address).Distinct().Count() != 1
                || !targetsByVerse.TryGetValue(predicates[0].Target.Token.Address, out var verse))
            {
                continue;
            }

            var clauseType = places[0]!.Value.ClauseType;
            var verbless = VerblessClauses.Contains(clauseType ?? string.Empty);
            var verb = predicates.FirstOrDefault(anchor => IsHebrewFinite(anchor.Target) && anchor.Target.Token.StrongNumber != HebrewBe);
            if (!verbless && verb is null)
            {
                continue;
            }

            var clause = verse.Where(other => clauses.Of(other)?.Clause == places[0]!.Value.Clause).ToList();
            var subjectOutside = subjectPlaced is not null && clause.All(other => other.Token.Id != subjectPlaced.Target.Token.Id);
            if (subjectOutside && clause.Any(other => clauses.Of(other)?.Function == SubjectFunction)
                || clause.Any(other => WritesBe(other, clause, clauses, taken, subjectPlaced)))
            {
                continue;
            }

            if (!verbless)
            {
                var onTheVerb = new EvidentiaProposal(
                    word,
                    verb!.Target,
                    EvidentiaProposalKind.AttachedWord,
                    Math.Max(0, verb.Confidence - ConfidenceBelowHead),
                    new EvidentiaDecisionTrace(
                        "attached",
                        $"{EvidentiaAttachment.BeOnTheVerb} of '{verb.Source.Token.Surface}', on the finite verb its predicate was placed on in a {clauseType} clause",
                        verb.Trace?.Evidence ?? []),
                    verb);
                proposals.Add(onTheVerb);
                placedBySource[word.Token.Id] = onTheVerb;
                continue;
            }

            var complements = clause
                .Where(other => PredicateComplements.Contains(clauses.Of(other)!.Value.Function ?? string.Empty))
                .GroupBy(other => clauses.Of(other)!.Value.Phrase)
                .ToList();
            var complement = complements.FirstOrDefault(phrase => phrase.Any(other => anchors.Any(anchor => anchor.Target.Token.Id == other.Token.Id)))
                ?? complements.FirstOrDefault();
            if (complement is null || PhraseHead([.. complement]) is not { } placement || IsHebrewFinite(placement))
            {
                continue;
            }

            var role = "the head of its predicate complement";
            if (HebrewInterrogatives.Contains(HebrewPartOfSpeech(placement)))
            {
                role = "the interrogative of its predicate complement";
            }
            else if (DemonstrativeSubject(clause, clauses) is { } demonstrative)
            {
                placement = demonstrative;
                role = "its demonstrative subject";
            }

            var head = anchors.FirstOrDefault(anchor => anchor.Target.Token.Id == placement.Token.Id)
                ?? anchors.FirstOrDefault(anchor => complement.Any(other => other.Token.Id == anchor.Target.Token.Id))
                ?? anchors[0];
            var proposal = new EvidentiaProposal(
                word,
                placement,
                EvidentiaProposalKind.AttachedWord,
                Math.Max(0, head.Confidence - ConfidenceBelowHead),
                new EvidentiaDecisionTrace(
                    "attached",
                    $"{EvidentiaAttachment.CopulaOnThePredicate} of '{head.Source.Token.Surface}', on {role} in a {clauseType} clause",
                    head.Trace?.Evidence ?? []),
                head);
            proposals.Add(proposal);
            placedBySource[word.Token.Id] = proposal;
        }
    }

    /// <summary>
    /// Whether a word of the clause is the original's own <em>is</em>: יֵשׁ, אַיִן, היה, or a הוּא no word took that
    /// stands beside another subject (<em>the LORD, he is God</em>) rather than being the subject itself.
    /// </summary>
    private static bool WritesBe(
        EvidentiaAnalysis word,
        IReadOnlyList<EvidentiaAnalysis> clause,
        EvidentiaClauses clauses,
        IReadOnlySet<long> taken,
        EvidentiaProposal? subject) =>
        WrittenBeFunctions.Contains(clauses.Of(word)?.Function ?? string.Empty)
        || word.Token.StrongNumber == HebrewBe
        || word.Token.StrongNumber is { } number && HebrewCopulaPronouns.Contains(number) && !taken.Contains(word.Token.Id)
            && (subject is not null && subject.Target.Token.Id != word.Token.Id
                || clause.Any(other => clauses.Of(other)?.Function == SubjectFunction
                    && other.Token.StrongNumber is not (HebrewArticle or Vav)
                    && !HebrewCopulaPronouns.Contains(other.Token.StrongNumber ?? string.Empty)));

    /// <summary>What these words were placed on in the original, where a rule of their own placed them.</summary>
    private static List<EvidentiaProposal> Placed(
        IEnumerable<EvidentiaAnalysis?> words, IReadOnlyDictionary<long, EvidentiaProposal> placedBySource) =>
        [.. words
            .OfType<EvidentiaAnalysis>()
            .DistinctBy(word => word.Token.Id)
            .Select(word => placedBySource.GetValueOrDefault(word.Token.Id))
            .OfType<EvidentiaProposal>()
            .Where(placed => placed.Kind != EvidentiaProposalKind.AttachedWord
                && placed.Target.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))];

    /// <summary>
    /// The word a phrase is about: its first word past a conjunction, an article and a preposition, so the noun of
    /// a prepositional phrase (<em>his delight is in the law</em> on תֹורַת, the בְּ being <em>in</em>'s), and the
    /// preposition itself where it is all the phrase writes (<em>I am with you</em> on עִמְּךָ).
    /// </summary>
    private static EvidentiaAnalysis? PhraseHead(IReadOnlyList<EvidentiaAnalysis> phrase)
    {
        var words = phrase.OrderBy(word => word.Token.Position)
            .Where(word => word.Token.StrongNumber is not (HebrewArticle or Vav) && HebrewPartOfSpeech(word) is not ("art" or "conj"))
            .ToList();
        return words.FirstOrDefault(word => HebrewPartOfSpeech(word) != "prep") ?? words.FirstOrDefault();
    }

    /// <summary>
    /// The demonstrative a clause's subject is, where it is one and nothing else but כֹּל: אֵלֶּה of <em>These are
    /// the sons</em>, and of כָּל־אֵלֶּה, <em>all these</em>; not זֶה of <em>this Philistine</em>, whose subject is a noun.
    /// </summary>
    private static EvidentiaAnalysis? DemonstrativeSubject(IReadOnlyList<EvidentiaAnalysis> clause, EvidentiaClauses clauses)
    {
        var subject = clause
            .Where(word => clauses.Of(word)?.Function == SubjectFunction
                && word.Token.StrongNumber is not (HebrewArticle or Vav or HebrewAll)
                && HebrewPartOfSpeech(word) is not ("art" or "conj"))
            .ToList();
        return subject.Count > 0 && subject.All(word => HebrewPartOfSpeech(word) == HebrewDemonstrative) ? subject[^1] : null;
    }

    private static string HebrewPartOfSpeech(EvidentiaAnalysis word) => word.PartOfSpeech ?? word.Token.PartOfSpeech ?? string.Empty;

    private static bool IsHebrewFinite(EvidentiaAnalysis word) => Feature(word, "tense") is { } tense && HebrewFiniteTenses.Contains(tense);

    /// <summary>
    /// The predicate English writes before its copula, where it does: <em>Great</em> of <em>Great is his glory</em>,
    /// but not <em>third</em> of <em>the third was Shammah</em>, which an article makes the subject.
    /// </summary>
    private static EvidentiaAnalysis? FrontedPredicate(IReadOnlyList<EvidentiaAnalysis> words, int index)
    {
        if (index == 0 || Punctuated(words[index - 1]))
        {
            return null;
        }

        var before = words[index - 1];
        var opening = index == 1 || Punctuated(words[index - 2]) || OpensTheSentence(before);
        var determined = index > 1 && !Punctuated(words[index - 2]) && Articles.Contains(words[index - 2].Token.Surface);
        return !determined && (Class(before) == "adj" || Class(before) == "verb" && Feature(before, "VerbForm") == "Part")
            || opening && FrontedQuestions.Contains(before.Token.Surface.TrimStart(OpeningQuotes))
                ? before
                : null;
    }

    /// <summary>
    /// The noun a phrase after <em>of</em> is about, past the modifiers English writes before it: <em>sons</em> of
    /// <em>Noah's sons</em>, <em>harvest</em> of <em>the barley harvest</em>, <em>wood</em> of <em>gopher wood</em>.
    /// Hebrew writes those modifiers after their noun, so the construct before <em>of</em> governs the noun.
    /// </summary>
    private static int NounOfThePhrase(IReadOnlyList<EvidentiaAnalysis> words, int index)
    {
        var at = index;
        while (words[at].Token.Relation is "compound" or "amod" or "nmod:poss" && !Punctuated(words[at])
               && Syntactic(words, words[at].Token.SyntacticHead) is { } head
               && IndexOf(words, head) is var to && to > at && to - index <= PhraseReach)
        {
            at = to;
        }

        return at;
    }

    private static bool Attach(
        IReadOnlyList<EvidentiaAnalysis> words,
        Dictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlyDictionary<EvidentiaAddress, List<EvidentiaAnalysis>> targetsByVerse,
        HashSet<long> taken,
        List<EvidentiaProposal> proposals,
        bool definiteNouns,
        bool ofOnTheConstruct)
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
            if (ofOnTheConstruct && found is not null && attachment == EvidentiaAttachment.Genitive
                && placedBySource.TryGetValue(words[index - 1].Token.Id, out var governing)
                && governing.Target.Token.Language.Equals(HebrewLanguage, StringComparison.OrdinalIgnoreCase))
            {
                found = (governing.Target, governing);
                head = words[index - 1];
            }
            if (found is null && attachment == EvidentiaAttachment.Genitive
                && Placed(EvidentiaAttachment.GenitivePreposition, index, head, words, placedBySource, targetsByVerse, taken) is { } lamed)
            {
                found = lamed;
                attachment = EvidentiaAttachment.GenitivePreposition;
            }

            if (found is null && definiteNouns && attachment == EvidentiaAttachment.Article
                && Placed(EvidentiaAttachment.DefiniteNoun, index, head, words, placedBySource, targetsByVerse, taken) is { } noun)
            {
                found = noun;
                attachment = EvidentiaAttachment.DefiniteNoun;
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

                // An auxiliary placed across its negation stands on the verb after the negation, and the ו of the
                // clause is written on the negation: וְלֹא עָנָם, but He did not answer.
                if (placedBySource.TryGetValue(other.Token.Id, out var head) && Attachment(head) != EvidentiaAttachment.AuxiliaryAcrossNegation)
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
        && Place(attachment, words, index, head, headProposal.Target, targetVerse, afterVerb, placedBySource, taken) is { } placement
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
                EvidentiaAttachment.DefiniteNoun => EvidentiaAttachmentPlacement.DefiniteByGrammar,
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
        EvidentiaAnalysis head,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        bool afterVerb,
        IReadOnlyDictionary<long, EvidentiaProposal> placedBySource,
        IReadOnlySet<long> taken)
    {
        var word = words[index];
        return Placement(attachment, rendering.Token.Language, word.Token.Language) switch
        {
            EvidentiaAttachmentPlacement.Rendering when attachment == EvidentiaAttachment.AuxiliaryVerb
                && (!IsEnglish(word) || word.Lemma == Be)
                && OwnAuxiliary(rendering, verse, beOnly: IsEnglish(word)) is { } own =>
                taken.Contains(own.Token.Id) ? null : own,
            EvidentiaAttachmentPlacement.Rendering when attachment == EvidentiaAttachment.AuxiliaryVerb
                && (Class(rendering) != "verb"
                    || IsEnglish(word) && (WritesNoTense(rendering) || !CouldTakeItsSubject(words, head, rendering))) => null,
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
                : Agreeing(attachment, word, head, rendering, verse, taken),
            EvidentiaAttachmentPlacement.Infinitive => Infinitive(rendering, verse, taken),
            EvidentiaAttachmentPlacement.BeBeside => BeBeside(rendering, verse, taken),
            EvidentiaAttachmentPlacement.ConjunctionBefore => ConjunctionBefore(rendering, verse, taken),
            EvidentiaAttachmentPlacement.ClauseConjunction => ClauseConjunction(rendering, verse, taken),
            EvidentiaAttachmentPlacement.LamedBefore =>
                PrepositionBefore(rendering, verse, taken) is { Token.StrongNumber: Lamed } lamed ? lamed : null,
            EvidentiaAttachmentPlacement.BeVerb =>
                rendering.Token.StrongNumber is { } be && BeVerbs.Contains(be) ? rendering : null,
            EvidentiaAttachmentPlacement.InfinitiveAbsolute => InfinitiveAbsoluteBeside(rendering, verse, taken),
            EvidentiaAttachmentPlacement.DefiniteByGrammar => DefiniteByGrammar(rendering, verse) ? rendering : null,
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

    /// <summary>
    /// A Hebrew noun its grammar makes definite and no article is written before: a name, or the head of a construct
    /// chain (דְּבַר יְהוָה) in which no article is written further on - where one is (מִשְׁפְּחוֹת הַכְּנַעֲנִי), the
    /// English <em>the</em> is as often that article's.
    /// </summary>
    private static bool DefiniteByGrammar(EvidentiaAnalysis rendering, IReadOnlyList<EvidentiaAnalysis> verse)
    {
        var at = IndexOf(verse, rendering);
        if (at < 0 || (at > 0 && verse[at - 1].Token.StrongNumber == HebrewArticle))
        {
            return false;
        }

        if (Class(rendering) == "propn")
        {
            return true;
        }

        if (Feature(rendering, "state") != "c")
        {
            return false;
        }

        for (var next = at + 1; next < verse.Count && next <= at + 3; next++)
        {
            var word = verse[next];
            if (word.Token.StrongNumber == HebrewArticle)
            {
                return false;
            }

            var nominal = Class(word) is "noun" or "propn" or "adj";
            if (!(word.Token.Trailer.Length == 0 || nominal && (Feature(word, "state") == "c" || next == at + 1)))
            {
                break;
            }
        }

        return true;
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
    /// An English form of <em>be</em> goes the same way (<em>I was mourning</em> on the הָיִיתִי beside
    /// מִתְאַבֵּל) and only for <em>be</em>: the other English auxiliaries do not translate it, and
    /// <paramref name="beOnly"/> leaves out the Greek <em>about to</em>.
    /// </summary>
    private static EvidentiaAnalysis? OwnAuxiliary(
        EvidentiaAnalysis rendering, IReadOnlyList<EvidentiaAnalysis> verse, bool beOnly = false)
    {
        var at = IndexOf(verse, rendering);
        var greek = rendering.Token.Language.Equals(GreekLanguage, StringComparison.OrdinalIgnoreCase);
        var before = verse.Skip(Math.Max(0, at - OwnAuxiliaryReach)).Take(Math.Min(at, OwnAuxiliaryReach)).Reverse();
        if (greek && IsInfinitive(rendering))
        {
            return beOnly ? null : before.FirstOrDefault(word => word.Token.StrongNumber == GreekAboutTo);
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

    /// <summary>An infinitive, construct or absolute.</summary>
    private static bool WritesNoTense(EvidentiaAnalysis word) =>
        IsInfinitive(word) || string.Equals(Feature(word, "tense"), InfinitiveAbsoluteTense, StringComparison.OrdinalIgnoreCase);

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
        EvidentiaAnalysis head,
        EvidentiaAnalysis rendering,
        IReadOnlyList<EvidentiaAnalysis> verse,
        IReadOnlySet<long> taken) =>
        attachment switch
        {
            EvidentiaAttachment.SubjectPronoun when Class(rendering) == "verb"
                && (EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: false)
                    || EvidentiaPersonAgreement.IsInfinitive(rendering) && EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: true))
                && !WritesTheSubjectApart(pronoun, rendering, verse, taken) => rendering,
            // The object of a verb is written in the verb: on a noun the suffix is whose it is.
            EvidentiaAttachment.ObjectPronoun when Class(head) == "verb" && Class(rendering) != "verb" => null,
            EvidentiaAttachment.PossessivePronoun or EvidentiaAttachment.ObjectPronoun
                when EvidentiaPersonAgreement.Agrees(pronoun, rendering, suffix: true) => rendering,
            _ => null,
        };

    /// <summary>
    /// Whether the verb the head was placed on could have the subject the parse gives the English verb. A
    /// verb that names no person - a participle, an infinitive - takes any; one that names a person takes
    /// a pronoun of that person, number and gender, and a noun only in the third person. Where the parse
    /// gives no subject, or one that says nothing of person (<em>whoever</em>), nothing is refused; a
    /// pronoun outside the nominative is no subject whatever the parse made of it.
    /// </summary>
    private static bool CouldTakeItsSubject(
        IReadOnlyList<EvidentiaAnalysis> words,
        EvidentiaAnalysis verb,
        EvidentiaAnalysis rendering)
    {
        if (Feature(rendering, "person") is not { } person)
        {
            return true;
        }

        var subjects = words
            .Where(other => other.Token.SyntacticHead == verb.Token.Id
                && other.Token.Relation is SubjectRelation or PassiveSubjectRelation
                && !(Feature(other, "Case") is { } stated && !stated.Equals(NominativeCase, StringComparison.OrdinalIgnoreCase)))
            .ToList();
        return subjects.Count == 0 || subjects.Any(subject => Class(subject) switch
        {
            "pron" when Feature(subject, "PronType") == PersonalPronounType && EvidentiaPersonAgreement.Person(subject) is not null =>
                EvidentiaPersonAgreement.Agrees(subject, rendering, suffix: false),
            "noun" or "propn" => EvidentiaMorphologyLabels.Feature(person, rendering.Token.Language) == ThirdPerson,
            _ => true,
        });
    }

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
