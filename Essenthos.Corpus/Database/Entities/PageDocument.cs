using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What the server writes one page from, for one page and one site language, made at build (TSK-1670): a record's or a
/// Strong number's page, with the API answers the page carries, so a page asked for reads one row instead of composing
/// it from forty or a hundred statements. The page is written from it per request, so the shell and the wording are the
/// running build's own. Pages by their address, never by row id.
/// </summary>
[PrimaryKey(nameof(Kind), nameof(Language), nameof(Address))]
public class PageDocument
{
    /// <summary>Which kind of page: <c>record</c> or <c>strong</c>.</summary>
    public required string Kind { get; set; }

    /// <summary>The site language's code (<c>en</c>, <c>uk</c>, <c>de</c>, <c>es</c>).</summary>
    public required string Language { get; set; }

    /// <summary>The record's slug or the Strong number.</summary>
    public required string Address { get; set; }

    /// <summary>The document, gzip-compressed.</summary>
    public required byte[] Body { get; set; }

    /// <summary>The first 16 hex digits of the SHA-256 of the uncompressed document.</summary>
    public required string Hash { get; set; }
}
