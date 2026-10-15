using Npgsql;

namespace Essenthos.Core.Loading.Links.Evidentia;

/// <summary>
/// Where an original's word stands in its own analysis: its clause, what kind of clause BHSA types it as
/// (<c>NmCl</c>, <c>AjCl</c>, <c>Ptcp</c>, <c>WayX</c> …), its phrase and what that phrase does in the clause
/// (<c>Subj</c>, <c>PreC</c>, <c>Pred</c>, <c>Nega</c>, <c>Exst</c> …).
/// </summary>
internal readonly record struct EvidentiaClausePlace(long Clause, string? ClauseType, long Phrase, string? Function);

/// <summary>
/// The clauses and phrases of the original's words a passage reads, as the text's own syntax names them. A
/// word the analysis puts in no clause or no phrase is not here.
/// </summary>
internal sealed class EvidentiaClauses(IReadOnlyDictionary<long, EvidentiaClausePlace> places)
{
    private const string Load =
        """
        SELECT wgw.word_id, wg.kind, wg.id, wg.features ->> 'type', wg.features ->> 'function'
        FROM word_group_word wgw
        JOIN word_group wg ON wg.id = wgw.word_group_id
        WHERE wgw.word_id = ANY(@words) AND wg.kind IN ('clause', 'phrase')
        """;

    public static EvidentiaClauses Of(params (long Word, EvidentiaClausePlace Place)[] words) =>
        new(words.ToDictionary(word => word.Word, word => word.Place));

    public EvidentiaClausePlace? Of(EvidentiaAnalysis word) =>
        places.TryGetValue(word.Token.Id, out var place) ? place : null;

    public static async Task<EvidentiaClauses> Read(
        NpgsqlConnection connection,
        IReadOnlyCollection<long> words,
        CancellationToken cancellationToken)
    {
        var read = new Dictionary<long, (long Clause, string? Type, long Phrase, string? Function)>(words.Count);
        await using var command = new NpgsqlCommand(Load, connection);
        command.Parameters.AddWithValue("words", words.ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var word = reader.GetInt64(0);
            read.TryGetValue(word, out var standing);
            read[word] = reader.GetString(1) == "clause"
                ? standing with { Clause = reader.GetInt64(2), Type = reader.IsDBNull(3) ? null : reader.GetString(3) }
                : standing with { Phrase = reader.GetInt64(2), Function = reader.IsDBNull(4) ? null : reader.GetString(4) };
        }

        return new EvidentiaClauses(read
            .Where(word => word.Value.Clause != 0 && word.Value.Phrase != 0)
            .ToDictionary(word => word.Key, word => new EvidentiaClausePlace(
                word.Value.Clause, word.Value.Type, word.Value.Phrase, word.Value.Function)));
    }
}
