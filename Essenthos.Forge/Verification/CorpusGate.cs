namespace Essenthos.Core.Verification;

/// <summary>
/// Whether a corpus may be served. One judgement, used by <c>forge verify</c>, by <c>forge release</c>
/// before it dumps anything, and by <c>forge publish</c> against the restored copy on the target before
/// anything points at it — so that the three cannot drift into three different ideas of "good enough".
///
/// The integrity checks are not measurements with a range: each counts a shape no correct load
/// produces, so anything above zero fails. The floor is set below where the corpus already stands,
/// over the books alignment has reached; its job is to catch a load or a restore that lost
/// something, not to be an aspiration, and not to fail a text for not being aligned yet.
/// </summary>
internal static class CorpusGate
{
    public static bool Pass(CorpusMeasures measures, double floor, ILogger logger)
    {
        if (measures.Broken > 0)
        {
            logger.LogError(
                "{Broken} integrity checks found something, and every one of them should find nothing",
                measures.Broken);
            return false;
        }

        // The floor holds the books alignment has reached. A book nobody has aligned yet is named,
        // not failed: it is work to do, and a newly loaded text would otherwise fail the build by
        // being loaded. What a load loses in a book already aligned still counts, here and in the
        // comparison with the run before.
        if (measures.UnalignedWords > 0)
        {
            logger.LogWarning(
                "{Words} words in {Books} books of a linked text are not aligned yet and are outside the floor: {Unaligned}",
                measures.UnalignedWords, measures.Unaligned.Count,
                string.Join("; ", measures.Unaligned.GroupBy(u => u.Text)
                    .Select(text => $"{text.Key} " + string.Join(", ", text.Select(u => $"{u.Book} ({u.Words})")))));
        }

        if (measures.Aligned < floor)
        {
            logger.LogError(
                "{Aligned:P1} of the words in the books alignment has reached reach a witness or are shown to have none, below the " +
                "floor of {Floor:P1} ({Rendered:P1} over every linked text). Either the load lost something, or the floor is " +
                "stale and should be raised deliberately",
                measures.Aligned, floor, measures.Rendered);
            return false;
        }

        logger.LogInformation(
            "{Aligned:P1} of the words in the books alignment has reached reach a witness or are shown to have none, floor " +
            "{Floor:P1}; {Rendered:P1} over every linked text; the weakest section of any one text reaches {Weakest:P1}",
            measures.Aligned, floor, measures.Rendered, measures.Weakest);
        return true;
    }
}
