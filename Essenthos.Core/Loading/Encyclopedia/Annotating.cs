using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;

namespace Essenthos.Core.Loading.Encyclopedia;

/// <summary>
/// The steps every annotation goes through once something has decided which Hebrew word names whom:
/// carry the answer along the links into the other texts, write the row, and write the claim that
/// says what asserted it.
///
/// Written once because the three things that produce annotations — a Strong number that resolves,
/// a model that read the verse, and a person who ruled — differ only in what they put in the seed.
/// The carrying rule is the same for all of them and has to stay the same: one hop, always from the
/// Hebrew, never onto a word two Hebrew words disagree about, onto the head of what the link names
/// unless the link names one name written several times, and the link's own confidence multiplied
/// into the annotation's so that a word reached by a guess is never stored as firmly as one reached
/// by a source's own mapping.
/// </summary>
internal static class Annotating
{
    /// <summary>
    /// The seed a caller fills and everything below reads.
    ///
    /// <c>confidence</c> is null exactly where a person or a source settled it, which is the same
    /// rule the tables themselves enforce. <c>corroborated</c> is whether the encyclopedia's own
    /// list of verses independently names that entity in this word's verse. <c>through</c> and
    /// <c>link</c> are null on a seed and filled on a word the links reached: the seed word it was
    /// reached from, and what the link crossed to reach it was worth.
    ///
    /// <para>
    /// It drops itself at commit. A temporary table outlives its transaction and belongs to the
    /// connection, and connections here are pooled — so without this, two loaders that both write
    /// annotations in one process fail on the second one with <em>relation already exists</em>,
    /// which is a start-up crash a long way from its cause.
    /// </para>
    /// </summary>
    public const string Workspace =
        """
        CREATE TEMP TABLE pending_annotation (
            word_id bigint PRIMARY KEY,
            entity_id integer NOT NULL,
            confidence double precision,
            corroborated boolean NOT NULL,
            note text NOT NULL,
            through bigint,
            link double precision)
        ON COMMIT DROP
        """;

    /// <summary>
    /// How every carried annotation's note begins, which is what tells a row the links reached from
    /// the seed it was reached from.
    /// </summary>
    public const string CarriedNote = "through %";

    /// <summary>
    /// The confidence below which a link is not enough on its own to put a name on a word.
    ///
    /// It is not a floor, and nothing is dropped for being faint alone: a faint link is often the
    /// only account the corpus has of a word, and a great many faint ones are true. Shebnah at
    /// 0.69 and Esau at 0.66 sit here, and so do the 250 places the Ukrainian <em>Бог</em> stands
    /// for <em>адонай</em>, none of which reaches 0.59. What this marks is a link weak enough to
    /// lose to a better one, and losing is the only thing that happens to it.
    /// </summary>
    public const double Faint = 0.70;

    /// <summary>
    /// The confidence at which a link is taken to be the rendering, so that a faint link to the
    /// same witness word in the same verse has nothing left to explain.
    ///
    /// The gap between this and <see cref="Faint"/> is the point. Two words of one text can both
    /// render one Hebrew name — <em>of Abinoam</em> is two words in the King James — and a rule
    /// that dropped the weaker of any pair would take the second half of every such rendering. A
    /// word that is part of the rendering scores near the one beside it; a word the aligner had
    /// nowhere else to put does not.
    /// </summary>
    public const double Firm = 0.90;

    /// <summary>
    /// An English possessive, which is where a name stops and the thing it possesses begins:
    /// <em>Terah's</em> in <em>Terah's lifetime</em>, <em>Jerusalem's</em> in <em>for Jerusalem's
    /// sake</em>. The plural form ends at the apostrophe, so the <c>s</c> is optional, and the
    /// King James writes <em>LORD'S</em>, so the match ignores case.
    /// </summary>
    private const string Possessive = "[''’]s?$";

    /// <summary>
    /// Everything a spelling is compared without — the apostrophe of a possessive, the hyphen of
    /// <em>Beth-shemesh</em>, the point of an abbreviation.
    /// </summary>
    private const string Ornament = "[^[:alnum:]]";

    /// <summary>
    /// The names the encyclopedia records for one entity, each taken whole.
    ///
    /// A label of several words contributes nothing, and that is the point rather than a
    /// simplification: the Nile's <em>River of Egypt</em> and the Euphrates' <em>the great
    /// river</em> would otherwise put <em>of</em> and <em>the</em> among the spellings a person is
    /// known by, and a rule that moves a naming towards a name would move it onto the article.
    /// A title does the same with <em>servant</em> and <em>king</em>, so titles are left out
    /// entirely; what is wanted here is what somebody is called, not what they are.
    ///
    /// <para>
    /// The record's own name and its slug are names as much as the label rows are, and the slug's
    /// trailing number is the encyclopedia telling two men of one name apart rather than part of
    /// either name.
    /// </para>
    /// </summary>
    private const string Names =
        """
        SELECT lower(known.spelling) AS spelling
        FROM (
            SELECT e.name AS spelling FROM entity e WHERE e.id = seed.entity_id
            UNION ALL
            SELECT regexp_replace(e.slug, '-[0-9]+$', '') FROM entity e WHERE e.id = seed.entity_id
            UNION ALL
            SELECT n.label FROM entity_name n
            WHERE n.entity_id = seed.entity_id AND coalesce(n.kind, '') NOT IN ('title', 'description')
            UNION ALL
            SELECT n.hebrew_transliterated FROM entity_name n
            WHERE n.entity_id = seed.entity_id AND coalesce(n.kind, '') NOT IN ('title', 'description')
            UNION ALL
            SELECT n.greek_transliterated FROM entity_name n
            WHERE n.entity_id = seed.entity_id AND coalesce(n.kind, '') NOT IN ('title', 'description')) known
        WHERE known.spelling ~ '^[[:alnum:]]+$'
        """;

    /// <summary>
    /// How alike a word and one of those names must be before the name is allowed to move the
    /// naming off the head. Trigram similarity, so it is a fraction and not a distance.
    ///
    /// <para>
    /// Half is where the measurement puts the line. Below it the corpus offers 86 displacements and
    /// 83 of them are the word <em>the</em>, which shares two trigrams with the Greek
    /// <em>theou</em> and scores 0.43 against it; <em>and</em> against <em>Andrew</em> and
    /// <em>thee</em> against <em>theou</em> are the rest. Above it there are 217 and they are the
    /// entity's own spelling or a form of it — <em>Canaanitish</em> at 0.53, <em>Benjamites</em> at
    /// 0.71, and the exact matches at 1. What the line costs is five displacements the corpus
    /// would have been right to make: <em>Dan</em> against <em>Danites</em> and <em>Judah</em>
    /// against <em>Judahites</em> at 0.33 and 0.45, and <em>Pochereth</em> at 0.45. Those keep the
    /// head, which is where they already were.
    /// </para>
    ///
    /// <para>
    /// Written as text rather than as a number because it is spliced into SQL, and a double
    /// formatted under a comma locale is not a number Postgres can read.
    /// </para>
    /// </summary>
    private const string Convincing = "0.5";

    /// <summary>
    /// The word of a link's other side that the naming lands on, where the link names several and
    /// only one of them can be the name. <see cref="Reached"/> is what decides which case this is.
    ///
    /// A link names a set of words on each side, and a source's own translation table routinely
    /// puts a phrase opposite a single original word, because that is what translation does: תֶּרַח
    /// in construct is <em>of Terah</em>, and the table has nowhere but the name to park the
    /// <em>When</em> that opens the clause. Handing the entity to every word of that set is what
    /// annotates <em>the</em>, <em>of</em> and <em>When</em> as people.
    ///
    /// <para>
    /// The naming goes to the set's head, and the head is its last word. That is not a guess about
    /// English: it is the ruled word in every one of the 332 groups a person has decided, and where
    /// it and the longest word disagree — 1,902 words of the two English texts — the last word is
    /// the name and the longest is <em>then</em>, <em>from</em>, <em>when</em> or
    /// <em>against</em>. The original sides read the same way, an article or a preposition standing
    /// before the name it is prefixed to rather than after it.
    /// </para>
    ///
    /// <para>
    /// The exception is a possessive, which closes the name from the other end: in <em>Terah's
    /// lifetime</em> the last word is the thing possessed and the name is the word before it. Only
    /// the word immediately before the last one is read that way — further back and the group is a
    /// clause rather than a possessive phrase, and its head is the last word again.
    /// </para>
    ///
    /// <para>
    /// Position decides only where the entity's own name does not. Where the source grouped two
    /// names into one correspondence the head is the wrong one of them — the Berean puts
    /// <em>Tahrea and Ahaz</em> opposite <em>וְ תַחְרֵעַ</em>, so the last word says Ahaz is Tahrea
    /// and leaves Tahrea unmarked, which reads as a fact where the article read as noise. So a word
    /// the encyclopedia records as this entity's name takes the naming from the head, and nothing
    /// else does: below <see cref="Convincing"/> every word of the group scores nothing and the
    /// order is the positional one, untouched.
    /// </para>
    /// </summary>
    public const string Head =
        $"""
         SELECT peer.word_id
         FROM (
             SELECT lw.word_id, hw.verse_id, hw.position,
                    hw.text ~* '{Possessive}'
                        AND row_number() OVER (ORDER BY hw.verse_id DESC, hw.position DESC) = 2
                        AS closes,
                    coalesce((
                        SELECT max(similarity(
                            lower(regexp_replace(hw.text, '{Ornament}', '', 'g')), known.spelling))
                        FROM ({Names}) known), 0) AS named
             FROM link_word lw
             JOIN word hw ON hw.id = lw.word_id
             WHERE lw.link_id = mine.link_id AND lw.side <> mine.side) peer
         ORDER BY CASE WHEN peer.named >= {Convincing} THEN peer.named ELSE 0 END DESC,
                  peer.closes DESC, peer.verse_id DESC, peer.position DESC
         LIMIT 1
         """;

    /// <summary>
    /// A link that pairs several occurrences of one name with several words, which is the shape the
    /// numbering writes where it cannot say which of them answers which.
    ///
    /// <para>
    /// 1 Samuel 30:7 writes דָּוִד twice and <em>Давид</em> three times, and every one of the five
    /// carries H1732. The Synodal's printed numbering can only say that those three render those
    /// two, so <see cref="Loading.Links.StrongNumberMatch"/> writes one link naming all five and the
    /// settlement removes the aligner's one-to-one links it replaced. A head picked out of that set
    /// names one <em>Давид</em> and leaves the other two blank, which is not what the set says.
    /// </para>
    ///
    /// <para>
    /// <strong>What makes it safe is the number, on the seed's own side.</strong> A phrase a
    /// translation's table states is also several words opposite several — <em>the son of Terah</em>
    /// against <em>בֶּן תֶּרַח</em> — and there the naming must land on one word, because only one
    /// of the words on the witness's side is the name. Here every word on that side is one
    /// occurrence of the seed's own lexeme, so the set holds nothing but the name and every word
    /// opposite it renders the name. There are 15,380 such links against 90,534 phrases, and the
    /// test tells them apart without asking what wrote either.
    /// </para>
    ///
    /// <para>
    /// A seed word carrying no number is excluded rather than compared: two nulls are not the same
    /// lexeme, and BHSA leaves 9,949 words unnumbered.
    /// </para>
    /// </summary>
    private const string OneNameTwice =
        """
        origin.strong_number IS NOT NULL
        AND (SELECT count(*) FROM link_word ours
             WHERE ours.link_id = mine.link_id AND ours.side = mine.side) > 1
        AND (SELECT count(*) FROM link_word theirs
             WHERE theirs.link_id = mine.link_id AND theirs.side <> mine.side) > 1
        AND NOT EXISTS (
            SELECT 1 FROM link_word ours
            JOIN word said ON said.id = ours.word_id
            WHERE ours.link_id = mine.link_id AND ours.side = mine.side
              AND said.strong_number IS DISTINCT FROM origin.strong_number)
        """;

    /// <summary>
    /// How alike two words of one text must be before they are taken to be the same word in two
    /// inflections, which is what the several words opposite a repeated name are.
    ///
    /// <para>
    /// It is not <see cref="Convincing"/> and must not be: that compares a word against a citation
    /// form the encyclopedia records in another language, and this compares two spellings of one
    /// word in one language, where the stem is most of the string and only the ending differs.
    /// Measured over the sets the corpus actually holds, the two distributions do not overlap and
    /// do not come close to it — <em>Давид</em> against <em>Давиду</em> is 0.63, <em>Египет</em>
    /// against <em>Египте</em> 0.40, <em>Дана</em> against <em>Даном</em> 0.38, while every word
    /// that is not the name scores zero: the <em>к</em> of Joshua 19:13 against <em>Риммону</em>,
    /// the <em>sister's</em> of Acts 23:16 against <em>Paul's</em>, and Luther's <em>mir</em>,
    /// <em>warst</em>, <em>alles</em>, <em>uns</em>, <em>nicht</em> and <em>hatte</em> against the
    /// name beside them. The line is put where the gap is and not at either edge of it.
    /// </para>
    ///
    /// <para>
    /// Written as text rather than as a number because it is spliced into SQL, and a double
    /// formatted under a comma locale is not a number Postgres can read.
    /// </para>
    /// </summary>
    private const string SameWord = "0.3";

    /// <summary>
    /// The words of the other side a naming lands on: <see cref="Head"/>, and beside it every other
    /// word of the set that is the same word as the head, where the link pairs one name written
    /// several times with several words.
    ///
    /// <para>
    /// Both halves are needed. Without the first, a set names one <em>Давид</em> of three. Without
    /// the second, it names whatever else the numbering swept into the set — the Synodal tags the
    /// preposition <em>к</em> with Rimmon's number at Joshua 19:13, the King James tags
    /// <em>sister's</em> with Paul's at Acts 23:16, and Luther's tagging is looser still. A word of
    /// the set that is the name again is the thing the head rule was picking between; a word that
    /// is not is the thing the head rule was protecting the page from.
    /// </para>
    ///
    /// <para>
    /// Written as one expression because the carrying step and the step that takes a carried row
    /// back both have to ask it, and a withdrawal that found fewer words than the carry wrote would
    /// leave the King James saying what the Hebrew beside it no longer says.
    /// </para>
    /// </summary>
    public const string Reached =
        $"""
         SELECT alone.word_id
         FROM ({Head}) alone
         WHERE NOT ({OneNameTwice})
         UNION ALL
         SELECT together.id
         FROM ({Head}) alone
         JOIN word foremost ON foremost.id = alone.word_id
         JOIN link_word beside
              ON beside.link_id = mine.link_id AND beside.side <> mine.side
         JOIN word together ON together.id = beside.word_id
         WHERE {OneNameTwice}
           AND similarity(
                   lower(regexp_replace(together.text, '{Ornament}', '', 'g')),
                   lower(regexp_replace(foremost.text, '{Ornament}', '', 'g'))) >= {SameWord}
         """;

    /// <summary>
    /// The same annotations on the words each link says stand for one of the seeded words — by
    /// <see cref="Reached"/>, so the head of a phrase and every word of a repeated name alike.
    ///
    /// A word reached from two seeded words that name two different entities is left alone: the
    /// links disagree about who is named, and picking between them is the judgement none of these
    /// loaders makes. Where they agree, the strongest link decides, because being reached twice is
    /// not weaker than being reached once.
    ///
    /// <para>
    /// A seed a person settled carries no confidence, and crossing a link that is itself certain
    /// leaves it that way — the person decided who is named, and a mapping the translators state
    /// does not make that less true. Crossing a link that is not certain does add a number, because
    /// then the annotation is only as sure as the correspondence it travelled along.
    /// </para>
    ///
    /// <para>
    /// A faint link is refused where the name is already rendered firmly in the same verse of the
    /// same text. An aligner that cannot place a word attaches it to whatever it can score, and in a
    /// verse that names somebody that is often the name: the Russian <em>От</em> opening Genesis
    /// 10:13 was reached from Ludim at 0.69 while <em>Лудим</em> itself renders the word at 0.90
    /// two places later. The comparison is per seed word and per verse, before unanimity, so a
    /// leftover can neither stand beside the rendering nor veto it by disagreeing.
    /// </para>
    ///
    /// <para>
    /// The note names the text the seed word stands in rather than a text the caller declares, so
    /// one pass can carry seeds from several witnesses and each row still says where it came from.
    /// Takes <c>@faint</c> and <c>@firm</c>; <see cref="CarryAcrossLinks"/> supplies both.
    /// </para>
    /// </summary>
    public const string Carry =
        $"""
        WITH reached AS (
            SELECT other.word_id,
                   w.text_id,
                   w.verse_id,
                   seed.entity_id,
                   crossed.worth AS link,
                   CASE WHEN seed.confidence IS NULL
                        THEN nullif(crossed.worth, 1.0)
                        ELSE seed.confidence * crossed.worth END AS confidence,
                   l.method,
                   seed.word_id AS through,
                   witness.slug AS spoken_by
            FROM pending_annotation seed
            JOIN word origin ON origin.id = seed.word_id
            JOIN text witness ON witness.id = origin.text_id
            JOIN link_word mine ON mine.word_id = seed.word_id
            JOIN link l ON l.id = mine.link_id
            CROSS JOIN LATERAL (SELECT {LinkWorth} AS worth) crossed
            CROSS JOIN LATERAL ({Reached}) other
            JOIN word w ON w.id = other.word_id
            WHERE seed.through IS NULL
        ),
        rendered AS (
            SELECT through, text_id, verse_id, max(link) AS best
            FROM reached GROUP BY 1, 2, 3
        ),
        supported AS (
            SELECT r.*
            FROM reached r
            JOIN rendered d ON d.through = r.through
                 AND d.text_id = r.text_id AND d.verse_id = r.verse_id
            WHERE r.link >= @faint OR d.best < @firm
        ),
        unanimous AS (
            SELECT word_id FROM supported GROUP BY 1 HAVING count(DISTINCT entity_id) = 1
        ),
        strongest AS (
            SELECT DISTINCT ON (r.word_id) r.*
            FROM supported r JOIN unanimous u ON u.word_id = r.word_id
            ORDER BY r.word_id, coalesce(r.confidence, 1.0) DESC, r.through
        )
        INSERT INTO pending_annotation (word_id, entity_id, confidence, corroborated, note, through, link)
        SELECT s.word_id, s.entity_id, s.confidence, agreed.named,
               'through ' || s.spoken_by || ' word ' || s.through || ', linked by ' || s.method,
               s.through, s.link
        FROM strongest s
        JOIN word w ON w.id = s.word_id
        CROSS JOIN LATERAL (SELECT EXISTS (
            SELECT 1 FROM verse_reference r
            JOIN entity_verse ev ON ev.entity_id = s.entity_id
                 AND ev.canonical_book = r.canonical_book
                 AND ev.canonical_chapter = r.canonical_chapter
                 AND ev.canonical_verse = r.canonical_verse
            WHERE r.verse_id = w.verse_id AND r.is_primary) AS named) agreed
        ON CONFLICT (word_id) DO NOTHING
        """;

    /// <summary>
    /// The conclusion. A word that already names this entity keeps the row it has: another method
    /// arriving at the same answer is corroboration, and it belongs in the claims rather than in a
    /// second conclusion saying the same thing twice.
    /// </summary>
    public const string Settle =
        """
        INSERT INTO word_entity (word_id, entity_id, method, confidence, source, note)
        SELECT a.word_id, a.entity_id, @method, a.confidence, @source, a.note
        FROM pending_annotation a
        ON CONFLICT (word_id, entity_id) DO NOTHING
        """;

    /// <summary>
    /// What asserted it, written in the same transaction as the conclusion. An annotation nothing
    /// claims is invisible to every measure of agreement, which is the failure the link claims were
    /// already caught by once — and here it would also be an annotation with no model, no prompt
    /// version and no date, which is the one thing a reading must never be stored without.
    ///
    /// <para>
    /// It joins on the conclusion rather than being written beside it, so a reading that agreed with
    /// an annotation already there lands as a second claim on the existing row instead of being
    /// dropped by the conflict clause above.
    /// </para>
    /// </summary>
    public const string Claim =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, w.confidence, @source, w.note
        FROM word_entity a
        JOIN pending_annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// The verse list's own claim, wherever it independently names the same entity in the same
    /// verse.
    ///
    /// What it records is exactly what the source states — that the entity is named somewhere in
    /// this verse — so it is testimony and carries no confidence. Reaching from there to the word
    /// is the step the annotation's own number is for, and that number is deliberately not raised
    /// by agreement here: the list is known to put verses on the wrong man, so a reading agreeing
    /// with a wrong entry is a shared error rather than a better answer.
    /// </summary>
    public const string Corroboration =
        """
        INSERT INTO word_entity_claim (word_entity_id, method, confidence, source, note)
        SELECT a.id, @method, NULL, @source, @note
        FROM word_entity a
        JOIN pending_annotation w ON w.word_id = a.word_id AND w.entity_id = a.entity_id
        WHERE w.corroborated
        ON CONFLICT DO NOTHING
        """;

    /// <summary>
    /// Long enough for a pass over four and a half million words and their links, which is what the
    /// carrying step is. A start-up pass that throws does not fail its own step — it fails every
    /// step after it.
    /// </summary>
    public const int Patient = 1800;

    /// <summary>
    /// The seed, sent as a binary copy rather than as thousands of parameterised inserts. Ten
    /// thousand round trips is a minute of start-up on a corpus that already takes long enough.
    /// </summary>
    public static async Task Seed(
        NpgsqlConnection connection,
        IEnumerable<(long WordId, int EntityId, double? Confidence, bool Corroborated, string Note)> rows,
        CancellationToken cancellationToken)
    {
        await using var writer = await connection.BeginBinaryImportAsync(
            "COPY pending_annotation (word_id, entity_id, confidence, corroborated, note) FROM STDIN (FORMAT BINARY)",
            cancellationToken);

        foreach (var (wordId, entityId, confidence, corroborated, note) in rows)
        {
            await writer.StartRowAsync(cancellationToken);
            await writer.WriteAsync(wordId, NpgsqlDbType.Bigint, cancellationToken);
            await writer.WriteAsync(entityId, NpgsqlDbType.Integer, cancellationToken);
            if (confidence is { } sure)
            {
                await writer.WriteAsync(sure, NpgsqlDbType.Double, cancellationToken);
            }
            else
            {
                await writer.WriteNullAsync(cancellationToken);
            }

            await writer.WriteAsync(corroborated, NpgsqlDbType.Boolean, cancellationToken);
            await writer.WriteAsync(note, NpgsqlDbType.Text, cancellationToken);
        }

        await writer.CompleteAsync(cancellationToken);
    }

    /// <summary>
    /// Whether the encyclopedia's own verse list names each seeded entity in each seeded word's
    /// verse, asked of the whole seed at once rather than per word.
    /// </summary>
    public const string MarkCorroboration =
        """
        UPDATE pending_annotation a SET corroborated = TRUE
        FROM word w, verse_reference r, entity_verse ev
        WHERE w.id = a.word_id
          AND r.verse_id = w.verse_id AND r.is_primary
          AND ev.entity_id = a.entity_id
          AND ev.canonical_book = r.canonical_book
          AND ev.canonical_chapter = r.canonical_chapter
          AND ev.canonical_verse = r.canonical_verse
        """;

    /// <summary>
    /// What a link is worth to a name carried across it: its own confidence, or, where it pairs one
    /// word with one word, the best of the claims standing on it.
    ///
    /// <para>
    /// The row carries the confidence of the method with the highest standing, and that is not
    /// always the surest number on it. The Synodal's numbering pairs a name written twice in a verse
    /// in the order both texts write it, at 0.70, and where the aligner had already paired the same
    /// two words its 0.97 is folded in as a claim on that link: 25,703 of the 33,678 such links in
    /// Russian and Hebrew hold one. Two methods arriving at one pair are not less sure than the
    /// better of them, and multiplying the name by the weaker would draw both of Lamech's names in
    /// Genesis 4:23 below the line a reader is told to doubt.
    /// </para>
    ///
    /// <para>
    /// Only where the link is one word on each side. A link naming several words says they
    /// correspond as a set, and a claim folded into it may be about any pair within the set, so it
    /// cannot vouch for the one word a name lands on.
    /// </para>
    /// </summary>
    public const string LinkWorth =
        """
        CASE WHEN l.confidence IS NULL THEN 1.0
             WHEN (SELECT count(*) FROM link_word pair WHERE pair.link_id = l.id) = 2
             THEN greatest(l.confidence, coalesce(
                  (SELECT max(agreeing.confidence) FROM link_claim agreeing
                   WHERE agreeing.link_id = l.id), 0))
             ELSE l.confidence END
        """;

    /// <summary><see cref="Carry"/>, with the two thresholds it compares links against.</summary>
    public static Task CarryAcrossLinks(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken) =>
        Run(connection, transaction, Carry, cancellationToken, ("faint", Faint), ("firm", Firm));

    public static async Task Run(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(
            sql, connection, (NpgsqlTransaction)transaction.GetDbTransaction());
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        }

        command.CommandTimeout = Patient;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// What each text ended up with, counted by source rather than by method: two things here write
    /// annotations a person settled, and a total that mixed them would credit one with the other's
    /// work.
    /// </summary>
    public static async Task<IReadOnlyList<(string Text, int Words)>> ByText(
        NpgsqlConnection connection,
        IDbContextTransaction transaction,
        string source,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT t.slug, count(*) FROM word_entity a JOIN word w ON w.id = a.word_id " +
            "JOIN text t ON t.id = w.text_id WHERE a.source = @source GROUP BY 1 ORDER BY 2 DESC",
            connection,
            (NpgsqlTransaction)transaction.GetDbTransaction());
        command.Parameters.AddWithValue("source", source);
        command.CommandTimeout = Patient;

        var counts = new List<(string, int)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add((reader.GetString(0), (int)reader.GetInt64(1)));
        }

        return counts;
    }
}
