using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Endpoints;

namespace Essenthos.Core.Pages;

/// <summary>
/// What a page was composed under that can change without a new corpus: the text the page's language quotes (the owner's
/// choice of translation), whether generated pictures are shown, and the place's own map picture. A stored page is
/// written from only while all three are as they were; otherwise the page is composed as it is asked.
/// </summary>
internal sealed record PageConditions(string? Chosen, bool Generated, string? PlacePicture);

/// <summary>A verse a record's page quotes, by its frame address, with the text it is quoted in.</summary>
internal sealed record QuotedVerse(int Book, int Chapter, int Verse, string? Text);

/// <summary>A book a Strong number stands in and how often, by the book's frame ordinal.</summary>
internal sealed record StrongBookTally(int Book, int Count);

/// <summary>
/// Everything a record's page is written from except what the wording says (book names, the page's own words): what the
/// database says about the record in one site language, and the API answer the page carries.
/// </summary>
internal sealed record RecordDocument(
    int Version,
    string? Build,
    EntityKind Kind,
    string Slug,
    string Name,
    string? Line,
    IReadOnlyList<LinePart> Descriptor,
    int References,
    IReadOnlyList<QuotedVerse> Verses,
    string? Image,
    double? Latitude,
    double? Longitude,
    IReadOnlyList<string> SameAs,
    IReadOnlyList<OriginalName> Names,
    IReadOnlyList<KnownClause> KnownFor,
    PageConditions Conditions)
{
    /// <summary>The answers the page carries, as <see cref="PageData"/> writes them; kept beside the document, not in it.</summary>
    [JsonIgnore]
    public byte[]? Data { get; init; }
}

/// <summary>
/// Everything a Strong number's page is written from except what the wording says: the entry in one site language, how
/// the text that language quotes renders it and in which books, and the API answers the page carries.
/// </summary>
internal sealed record StrongDocument(
    int Version,
    string? Build,
    string Number,
    string? Lemma,
    string? Transliteration,
    string? Definition,
    string? Derivation,
    string? Outline,
    bool Rendered,
    int? Uses,
    string? Edition,
    IReadOnlyList<StrongRenderingCount> Renderings,
    IReadOnlyList<StrongBookTally> Books,
    PageConditions Conditions)
{
    /// <summary>The answers the page carries, as <see cref="PageData"/> writes them; kept beside the document, not in it.</summary>
    [JsonIgnore]
    public byte[]? Data { get; init; }
}

/// <summary>
/// Everything an occupation's page is written from except what the wording says: the occupation and its first people as
/// the page lists them, and the first page of people the reader's script asks for, carried in the page.
/// </summary>
internal sealed record OccupationDocument(
    int Version,
    string? Build,
    OccupationDetailResponse Occupation,
    PageConditions Conditions)
{
    /// <summary>The answers the page carries, as <see cref="PageData"/> writes them; kept beside the document, not in it.</summary>
    [JsonIgnore]
    public byte[]? Data { get; init; }
}

[JsonSourceGenerationOptions(JsonSerializerDefaults.Web, DefaultIgnoreCondition = JsonIgnoreCondition.Never)]
[JsonSerializable(typeof(RecordDocument))]
[JsonSerializable(typeof(StrongDocument))]
[JsonSerializable(typeof(OccupationDocument))]
internal sealed partial class PageDocumentJson : JsonSerializerContext;

/// <summary>
/// The stored form of a page's document (TSK-1670, table <c>page_document</c>, products <c>page-documents/{kind}/{language}</c>):
/// gzip of the document's JSON, then the record separator U+001E, then the answers the page carries exactly as the page
/// writes them. JSON never holds a raw U+001E, so the first one ends the document. The build composes these with the very
/// code a page asked for runs (<see cref="PageLoader"/>), so a page written from its document is the page composed as asked.
/// </summary>
internal static class PageDocuments
{
    public const string RecordKind = "record";

    public const string StrongKind = "strong";

    public const string OccupationKind = "occupation";

    /// <summary>Raise with any change to what a document holds or how a page is written from it, so older rows are not read.</summary>
    public const int Version = 3;

    /// <summary>
    /// The code that composes a document, and the only code that may write a page or answer from one: the commit this API was
    /// built from (<see cref="Endpoints.Build.Commit"/>). A document is composed by the Forge, which runs this assembly, and
    /// served by the API, which is this assembly built again, on the workstation or in the image; the two are the same code
    /// exactly when they name the same commit. A normal release is one commit: the Forge that builds the corpus, its page
    /// documents included, and the API image deployed beside it are both built from it, so every document is read. An API
    /// deployed on its own names a newer commit than the documents of the corpus it serves, and every page and answer is then
    /// composed as it is asked until a corpus built by that commit replaces them. A build that knows no commit (no git and no
    /// <c>SOURCE_COMMIT</c>) names nothing, and reads no document. The commit says nothing of uncommitted changes, so a corpus
    /// is released from committed code, as the release already requires of the Forge.
    /// </summary>
    public static readonly string? Build = Endpoints.Build.Commit;

    /// <summary>Whether a document of this version and composer may be read by this build.</summary>
    public static bool Readable(int version, string? build) => version == Version && build is not null && build == Build;

    private const byte Separator = 0x1E;

    public static JsonTypeInfo<RecordDocument> Record => PageDocumentJson.Default.RecordDocument;

    public static JsonTypeInfo<StrongDocument> Strong => PageDocumentJson.Default.StrongDocument;

    public static JsonTypeInfo<OccupationDocument> Occupation => PageDocumentJson.Default.OccupationDocument;

    /// <summary>The document and its answers as stored, the hash of the uncompressed bytes and their length.</summary>
    public static (byte[] Body, string Hash, int Length) Pack<T>(T document, byte[]? data, JsonTypeInfo<T> info)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(document, info);
        var plain = new byte[json.Length + (data is null ? 0 : 1 + data.Length)];
        json.CopyTo(plain, 0);
        if (data is not null)
        {
            plain[json.Length] = Separator;
            data.CopyTo(plain, json.Length + 1);
        }

        using var buffer = new MemoryStream();
        using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
        {
            gzip.Write(plain);
        }

        return (buffer.ToArray(), Convert.ToHexStringLower(SHA256.HashData(plain))[..16], plain.Length);
    }

    /// <summary>A stored body read back: the document, and the answers that followed it, if any.</summary>
    public static (T Document, byte[]? Data) Unpack<T>(byte[] body, JsonTypeInfo<T> info)
    {
        using var input = new GZipStream(new MemoryStream(body), CompressionMode.Decompress);
        using var plain = new MemoryStream();
        input.CopyTo(plain);
        var bytes = plain.GetBuffer().AsSpan(0, (int)plain.Length);
        var end = bytes.IndexOf(Separator);
        var document = JsonSerializer.Deserialize(end < 0 ? bytes : bytes[..end], info)
                       ?? throw new InvalidDataException("A stored page document is empty.");
        return (document, end < 0 ? null : bytes[(end + 1)..].ToArray());
    }
}
