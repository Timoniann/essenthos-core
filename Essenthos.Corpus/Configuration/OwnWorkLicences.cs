namespace Essenthos.Core.Configuration;

/// <param name="Id">What the site's settings name it by.</param>
/// <param name="Name">Its short name, as a reader recognises it.</param>
/// <param name="Url">Its text; null where there is none to follow.</param>
public sealed record OwnWorkLicence(string Id, string Name, string? Url);

/// <summary>
/// The licences the project owner may put Essenthos's own work under — the models it draws, the
/// descriptions it writes, the corrections and word links it works out — and the one it is under
/// while he has not decided, which is none that the site states. One setting for all of it, so a
/// decision made once reaches every credit line that names our work.
/// </summary>
public static class OwnWorkLicences
{
    public const string Undecided = "undecided";

    public static readonly IReadOnlyList<OwnWorkLicence> Options =
    [
        new("cc-by-4.0", "CC BY 4.0", "https://creativecommons.org/licenses/by/4.0/"),
        new("cc-by-sa-4.0", "CC BY-SA 4.0", "https://creativecommons.org/licenses/by-sa/4.0/"),
        new("cc-by-nc-4.0", "CC BY-NC 4.0", "https://creativecommons.org/licenses/by-nc/4.0/"),
        new("cc-by-nc-sa-4.0", "CC BY-NC-SA 4.0", "https://creativecommons.org/licenses/by-nc-sa/4.0/"),
        new("cc0-1.0", "CC0 1.0", "https://creativecommons.org/publicdomain/zero/1.0/"),
        new("all-rights-reserved", "All rights reserved", null),
    ];

    /// <summary>Every id the setting may hold, undecided first.</summary>
    public static IReadOnlyList<string> Ids { get; } = [Undecided, .. Options.Select(o => o.Id)];

    /// <summary>The licence an id names; null while undecided, or for an id no option has.</summary>
    public static OwnWorkLicence? Find(string? id) => Options.FirstOrDefault(o => o.Id == id);
}
