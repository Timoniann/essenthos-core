using System.Text.Json.Nodes;
using Essenthos.Core.Configuration;

namespace Essenthos.Core.Desk;

/// <param name="Value">What the site does now.</param>
/// <param name="Default">What it does when the file says nothing.</param>
internal sealed record SiteSwitch(string Key, bool Value, bool Default);

internal sealed record SiteSwitchesResponse(IReadOnlyList<SiteSwitch> Settings);

internal sealed record SiteSwitchRequest(bool Value, string? Note);

/// <summary>
/// The site's switches as the owner sets them: the tracked file beside the API, which the API reads
/// again on the next request after it changes, so a switch takes effect on the local site at once
/// and on a published one with its next build.
/// </summary>
internal sealed class SiteSwitches(DeskPaths paths, ChangeLog log)
{
    public const string Section = "settings";

    private const string About =
        "Switches the project owner sets for the site in his console. The API serves them at /v1/settings and the " +
        "site reads them at runtime; a switch not named here is at its default. Every change made in the console is " +
        "also recorded in Resources/Essenthos/owner-changes.jsonl.";

    public SiteSwitchesResponse Read()
    {
        var values = SiteSettings.Read(paths.SiteSettings);
        return new SiteSwitchesResponse(SiteSettings.Catalogue.Select(s => new SiteSwitch(s.Key, values[s.Key], s.Default)).ToList());
    }

    /// <summary>Sets one switch; null where the catalogue has no switch by that name.</summary>
    public async Task<SiteSwitch?> Set(string key, SiteSwitchRequest request)
    {
        if (!SiteSettings.Knows(key))
        {
            return null;
        }

        var before = SiteSettings.Read(paths.SiteSettings)[key];
        await Write(key, JsonValue.Create(request.Value));
        await log.Append(Section, "switch", $"setting/{key}", JsonValue.Create(before), JsonValue.Create(request.Value), request.Note, null);
        return new SiteSwitch(key, request.Value, SiteSettings.Catalogue.Single(s => s.Key == key).Default);
    }

    /// <summary>
    /// Picks the text one of the site's choices names, and records it in the change log under the
    /// section the owner picked it in. The caller has made sure the text is one the choice can name.
    /// </summary>
    public async Task Choose(string key, string value, string section, string? label, string? note)
    {
        var before = SiteSettings.ReadChoices(paths.SiteSettings)[key];
        if (before == value)
        {
            return;
        }

        await Write(key, JsonValue.Create(value));
        await log.Append(section, "choice", $"setting/{key}", JsonValue.Create(before), JsonValue.Create(value), note, null, label);
    }

    private async Task Write(string key, JsonNode value)
    {
        if (!File.Exists(paths.SiteSettings))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(paths.SiteSettings)!);
            JsonFiles.Write(paths.SiteSettings, new JsonObject { ["about"] = About, ["settings"] = new JsonObject() });
        }

        await JsonFiles.Change(paths.SiteSettings, node =>
        {
            if (node["settings"] is not JsonObject settings)
            {
                node["settings"] = settings = [];
            }

            settings[key] = value;
            return (true, true);
        });
    }
}
