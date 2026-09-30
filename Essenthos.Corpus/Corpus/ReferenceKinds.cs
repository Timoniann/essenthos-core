namespace Essenthos.Core.Corpus;

/// <summary>
/// What a verse reference says of its record: that the verse names it, that the verse speaks of it
/// without the name — <em>the king of Babylon</em> for Nebuchadnezzar, <em>the Philistine</em> for
/// Goliath, <em>him</em> — or only that a source lists the verse as concerning it.
///
/// <para>
/// The second kind is a reading, and the row that holds it says so in its source: every row read that
/// way ends with <see cref="SpokenOfMark"/>, as every row of the witness starts with
/// <see cref="ShownVerses.Witness"/>. It stays the kind of the verse where the word that stands for
/// the record is annotated to it, since <em>king</em> naming Nebuchadnezzar is still not his name.
/// </para>
/// </summary>
internal static class ReferenceKinds
{
    public const string Named = "named";

    public const string SpokenOf = "spoken-of";

    public const string Concerning = "concerning";

    /// <summary>How the source of a row read as speaking of its record without the name ends.</summary>
    public const string SpokenOfMark = "the verse speaks of the record and does not name it";

    public static bool IsSpokenOf(string source) => source.EndsWith(SpokenOfMark, StringComparison.Ordinal);

    /// <summary>The kind of one verse, from every row the record has at it.</summary>
    public static string Of(IEnumerable<(string Source, bool Names)> rows)
    {
        var names = false;
        foreach (var (source, named) in rows)
        {
            if (IsSpokenOf(source))
            {
                return SpokenOf;
            }

            names |= named;
        }

        return names ? Named : Concerning;
    }
}
