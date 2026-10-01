using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;

namespace Essenthos.Core.Corpus;

/// <summary>
/// Which words of the corpus's links write another word's inflection or function, and which word
/// that is, as EVIDENTIA's attached-word verdicts say.
///
/// <para>
/// An attached word is written as an ordinary link from the word to what its rule placed it on, so
/// the links say <em>did</em> renders εἴδομεν and say nothing of <em>see</em>. Its decision knows
/// the rest: the attachment opens its rationale (<c>AuxiliaryVerb of 'see'</c>), and since the
/// decision records the pair it rests on, the head's word too. A word is attached only where it
/// landed on its head's own counterpart; one placed on a part of its own, <em>and</em> on the ו
/// before its head's rendering, renders that part and is an ordinary link.
/// </para>
///
/// <para>
/// A decision recorded before the head pair was kept, or written again from the ledger, which keeps
/// words by their address, names its head only by the surface in its rationale. There the head is
/// the word of that surface in the same verse, nearest first, that the run placed on the same
/// counterpart or that a link joins to it.
/// </para>
/// </summary>
internal static class AttachedWords
{
    /// <summary>The decision kind EVIDENTIA's attached-word resolver records.</summary>
    public const string DecisionKind = "attached-word";

    /// <summary>The attachments by which a word renders one word together with its head, rather than writing its inflection.</summary>
    public static readonly IReadOnlyList<string> PhraseAttachments = ["PhrasalParticle", "VerbOfItsParticle"];

    /// <summary>
    /// Marks every link word an approved attached-word verdict wrote with its role and head, of one run
    /// or of all of them. Idempotent: a word already marked is left as it is.
    /// </summary>
    public static FormattableString Mark(int? runId) =>
        $"""
         WITH attached AS (
             SELECT r.link_id, d.source_word_id AS word_id, d.target_word_id, d.run_id, d.rationale,
                    d.anchor_source_word_id, d.anchor_target_word_id, w.verse_id, w.position
             FROM evidentia_review r
             JOIN evidentia_decision d ON d.id = r.decision_id
             JOIN word w ON w.id = d.source_word_id
             WHERE d.kind = {DecisionKind} AND r.verdict = {EnumSpelling.Of(EvidentiaVerdict.Approved)} AND r.link_id IS NOT NULL
               AND ({runId}::integer IS NULL OR d.run_id = {runId}::integer)
         ),
         headed AS (
             SELECT a.link_id, a.word_id, a.anchor_source_word_id AS head_word_id, a.rationale
             FROM attached a
             WHERE a.anchor_source_word_id IS NOT NULL AND a.anchor_target_word_id = a.target_word_id
             UNION ALL
             (SELECT DISTINCT ON (a.link_id, a.word_id) a.link_id, a.word_id, h.id, a.rationale
              FROM attached a
              JOIN word h ON h.verse_id = a.verse_id AND h.id <> a.word_id
              WHERE a.anchor_source_word_id IS NULL
                AND starts_with(a.rationale, split_part(a.rationale, ' ', 1) || ' of ''' || h.text || '''')
                AND (EXISTS (SELECT 1 FROM evidentia_decision hd
                             WHERE hd.run_id = a.run_id AND hd.source_word_id = h.id
                               AND hd.target_word_id = a.target_word_id)
                     OR EXISTS (SELECT 1 FROM link_word hl
                                JOIN link_word ht ON ht.link_id = hl.link_id AND ht.side <> hl.side
                                WHERE hl.word_id = h.id AND ht.word_id = a.target_word_id))
              ORDER BY a.link_id, a.word_id, abs(h.position - a.position))
         )
         UPDATE link_word lw
         SET role = CASE WHEN split_part(headed.rationale, ' ', 1) = ANY ({PhraseAttachments.ToArray()})
                         THEN {EnumSpelling.Of(LinkWordRole.PhraseMember)}
                         ELSE {EnumSpelling.Of(LinkWordRole.Attached)} END,
             head_word_id = headed.head_word_id
         FROM headed
         WHERE lw.link_id = headed.link_id AND lw.word_id = headed.word_id AND lw.role IS NULL
         """;
}
