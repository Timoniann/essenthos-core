using System.Text.Json;
using System.Text.Json.Serialization;

namespace Essenthos.Core.Corpus;

/// <summary>
/// What <c>forge export</c> wrote, as the file <c>manifest.json</c> beside the files: the one document
/// both the Forge that writes it and the API that lists it read, so neither can drift from the other.
/// </summary>
/// <param name="Format">The version of this shape, raised when a field changes meaning.</param>
/// <param name="Fingerprint">
/// SHA-256 over the checksum of every file, in path order. Two exports with the same value hold the
/// same bytes, whichever folder they were written to.
/// </param>
/// <param name="Corpus">Which corpus the files were taken from.</param>
/// <param name="Texts">Every text exported, in slug order.</param>
/// <param name="Withheld">Every text of the corpus that is not in the export, and why.</param>
internal sealed record DownloadsManifest(
    int Format,
    DateTimeOffset GeneratedAt,
    string Fingerprint,
    ExportedCorpus Corpus,
    IReadOnlyList<ExportedText> Texts,
    IReadOnlyList<WithheldText> Withheld)
{
    public const string FileName = "manifest.json";

    public const int CurrentFormat = 1;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static DownloadsManifest? Parse(string json) => JsonSerializer.Deserialize<DownloadsManifest>(json, Json);
}

/// <param name="Release">The release the database was restored from, or null for a working copy no label describes.</param>
/// <param name="ManifestSha">SHA-256 of <c>Resources/MANIFEST.json</c>, which is itself a fingerprint of every source folder.</param>
/// <param name="MigrationHead">The last corpus migration applied.</param>
/// <param name="ForgeVersion">The Forge that wrote the export.</param>
internal sealed record ExportedCorpus(string? Release, string? ManifestSha, string MigrationHead, string ForgeVersion);

/// <param name="Slug">The text's identifier, which is also the folder its files are in.</param>
/// <param name="Kind">One of the corpus's text kinds, spelled as the API spells it.</param>
/// <param name="Redistribution">What the corpus records about passing the text on, spelled as the API spells it.</param>
/// <param name="Books">How many books the file holds.</param>
/// <param name="Verses">How many lines the file holds: one per verse.</param>
/// <param name="File">The text itself.</param>
/// <param name="Attribution">Who made it and under what terms, to be kept with the text wherever it goes.</param>
internal sealed record ExportedText(
    string Slug,
    string Name,
    string? NameNative,
    string Language,
    string Kind,
    string Redistribution,
    string? Licence,
    string? LicenceUrl,
    string? RightsHolder,
    string? Citation,
    int Books,
    int Verses,
    ExportedFile File,
    ExportedFile Attribution);

/// <param name="Path">Relative to the export's folder, with forward slashes.</param>
internal sealed record ExportedFile(string Path, long Bytes, string Sha256);

/// <param name="Reason">Why the text is not in this export, in a sentence for the person releasing.</param>
internal sealed record WithheldText(string Slug, string Redistribution, string Reason);
