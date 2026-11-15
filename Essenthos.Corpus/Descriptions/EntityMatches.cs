namespace Essenthos.Core.Descriptions;

/// <summary>The name or title a search found an entity by.</summary>
/// <param name="Name">The name as the record or the text has it: <em>Barnabas</em>, <em>Варнава</em>, <em>Son of David</em>.</param>
/// <param name="Kind">One of <see cref="MatchKinds"/>: another name of the same one, or a title.</param>
internal sealed record EntityMatchResponse(string Name, string Kind);

/// <summary>What a search can have found an entity by besides the name it is headed with.</summary>
internal static class MatchKinds
{
    /// <summary>Another name the same one is called by.</summary>
    public const string Name = "name";

    /// <summary>A title or description of it, which a name of somebody else may stand inside.</summary>
    public const string Title = "title";
}
