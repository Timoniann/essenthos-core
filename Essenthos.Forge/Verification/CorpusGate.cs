namespace Essenthos.Core.Verification;

/// <summary>
/// Whether a corpus may be served. One judgement, used by <c>forge verify</c>, by <c>forge release</c>
/// before it dumps anything, and by <c>forge publish</c> against the restored copy on the target before
/// anything points at it — so that the three cannot drift into three different ideas of "good enough".
///
/// The integrity checks are not measurements with a range: each counts a shape no correct load
/// produces, so anything above zero fails. The rendered floor is set below where the corpus already
/// stands; its job is to catch a load or a restore that lost something, not to be an aspiration.
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

        if (measures.Rendered < floor)
        {
            logger.LogError(
                "{Rendered:P1} of the words in a linked text reach a witness or are shown to have none, below the floor of {Floor:P1}. Either " +
                "the load lost something, or the floor is stale and should be raised deliberately",
                measures.Rendered, floor);
            return false;
        }

        logger.LogInformation(
            "{Rendered:P1} of the words in a linked text reach a witness or are shown to have none, floor {Floor:P1}; the weakest section of " +
            "any one text reaches {Weakest:P1}",
            measures.Rendered, floor, measures.Weakest);
        return true;
    }
}
