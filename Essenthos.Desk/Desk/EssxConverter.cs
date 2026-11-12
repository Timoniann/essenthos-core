using System.Diagnostics;
using Essenthos.Core.Products;
using Essenthos.Core.Products.Essx;
using Essenthos.Core.Products.Tools;

namespace Essenthos.Core.Desk;

internal sealed record EssxConvertedColumn(string Name, string Type, bool Nullable);

/// <param name="Id">The made file's name on the desk, to download it, open it or forget it by.</param>
/// <param name="Name">The file's name as it downloads: the source's name with the other extension.</param>
/// <param name="From">The source's name.</param>
/// <param name="Format">What was made: <c>essx</c> or <c>tsv</c>.</param>
internal sealed record EssxConversionView(
    string Id,
    string Name,
    string From,
    string Format,
    long Bytes,
    long Rows,
    IReadOnlyList<EssxConvertedColumn> Columns,
    bool Compressed,
    double Ms);

/// <summary>
/// The console's conversion of a table between its text shapes and ESSX, through the same conversion as
/// the <c>essx convert</c> tool: a TSV, CSV, JSONL or XML table into an .essx file, an .essx file into a
/// TSV. What it makes is written only into the console's cache, each in a folder of its own, and leaves
/// it as a download (or, for an .essx file, opened on the page); nothing is written beside the source,
/// and a product under Resources is only ever read. A made file goes when the page forgets it or a day
/// passes.
/// </summary>
internal sealed class EssxConverter(DeskPaths paths, EssxInspector inspector)
{
    private static readonly string[] IntoEssx = [".tsv", ".csv", ".jsonl", ".xml"];

    private const string Tsv = ".tsv";

    private const string SourceFolder = "source";

    private string Made => Path.Combine(paths.Cache, "essx", "converted");

    /// <summary>A table the owner dropped or picked: its bytes are put in a folder of their own, converted, then deleted.</summary>
    public async Task<EssxConversionView> Upload(string? name, bool compress, Stream body, CancellationToken cancellationToken)
    {
        var from = NameOf(name);
        var kind = Path.GetExtension(from).ToLowerInvariant();
        var target = kind == EssxLayout.Extension ? Tsv
            : IntoEssx.Contains(kind) ? EssxLayout.Extension
            : throw new ArgumentException($"'{from}' is not a table the desk converts: it takes .tsv, .csv, .jsonl or .xml into .essx, and .essx into .tsv.");
        var (id, folder) = NewFolder();
        var sourceFolder = Directory.CreateDirectory(Path.Combine(folder, SourceFolder)).FullName;
        var source = Path.Combine(sourceFolder, from);
        try
        {
            await using (var file = new FileStream(source, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1 << 20, FileOptions.Asynchronous))
            {
                if (target == EssxLayout.Extension)
                {
                    await SkipByteOrderMark(body, file, cancellationToken);
                }

                await body.CopyToAsync(file, 1 << 20, cancellationToken);
            }

            return Convert(id, folder, source, from, target, compress, table: null);
        }
        catch
        {
            Forget(id);
            throw;
        }
        finally
        {
            if (Directory.Exists(sourceFolder))
            {
                Directory.Delete(sourceFolder, recursive: true);
            }
        }
    }

    /// <summary>A file open on the page, a product or a dropped one, as a TSV of one of its tables; null when it is not open.</summary>
    public EssxConversionView? FromOpen(string openId, string? table)
    {
        if (inspector.Source(openId) is not { } open)
        {
            return null;
        }

        var (path, name) = open;
        var (id, folder) = NewFolder();
        try
        {
            return Convert(id, folder, path, NameOf(name), Tsv, compress: false, table);
        }
        catch
        {
            Forget(id);
            throw;
        }
    }

    /// <summary>The made file to download, or null when the desk has none by that id.</summary>
    public (string Path, string Name, string ContentType)? Download(string id)
    {
        if (MadeFile(id) is not { } path)
        {
            return null;
        }

        var tsv = path.EndsWith(Tsv, StringComparison.OrdinalIgnoreCase);
        return (path, Path.GetFileName(path), tsv ? "text/tab-separated-values; charset=utf-8" : "application/octet-stream");
    }

    /// <summary>A made .essx file, opened on the page as a dropped file is.</summary>
    public EssxFileView? Open(string id) =>
        MadeFile(id) is { } path && path.EndsWith(EssxLayout.Extension, StringComparison.OrdinalIgnoreCase)
            ? inspector.OpenCopy(path, Path.GetFileName(path))
            : null;

    public bool Forget(string id)
    {
        if (FolderOf(id) is not { } folder || !Directory.Exists(folder))
        {
            return false;
        }

        Directory.Delete(folder, recursive: true);
        return true;
    }

    private EssxConversionView Convert(string id, string folder, string source, string from, string target, bool compress, string? table)
    {
        var to = Path.Combine(folder, Path.GetFileNameWithoutExtension(from) + target);
        var clock = Stopwatch.StartNew();
        ConversionResult result;
        try
        {
            result = ProductConversion.Convert(source, to, new ConversionOptions
            {
                Table = string.IsNullOrWhiteSpace(table) ? null : table,
                Essx = EssxOptions.Default with { Codec = compress ? EssxLayout.CodecDeflate : EssxLayout.CodecNone },
            });
        }
        catch (Exception exception) when (exception is ProductFormatException or FormatException or OverflowException or ArgumentException or KeyNotFoundException)
        {
            throw new ProductFormatException(exception.Message
                .Replace(source, from, StringComparison.OrdinalIgnoreCase)
                .Replace(to, Path.GetFileName(to), StringComparison.OrdinalIgnoreCase));
        }

        return new EssxConversionView(
            id,
            Path.GetFileName(to),
            from,
            target.TrimStart('.'),
            new FileInfo(to).Length,
            result.Rows,
            [.. result.Columns.Select(column => new EssxConvertedColumn(column.Name, column.Type.ToString().ToLowerInvariant(), column.Nullable))],
            compress && target == EssxLayout.Extension,
            clock.Elapsed.TotalMilliseconds);
    }

    private (string Id, string Folder) NewFolder()
    {
        Directory.CreateDirectory(Made);
        foreach (var stale in Directory.EnumerateDirectories(Made).Where(dir => Directory.GetLastWriteTimeUtc(dir) < DateTime.UtcNow.AddDays(-1)))
        {
            try
            {
                Directory.Delete(stale, recursive: true);
            }
            catch (IOException)
            {
                // Still being downloaded; the next conversion sweeps it.
            }
        }

        var id = Guid.NewGuid().ToString("N");
        return (id, Directory.CreateDirectory(Path.Combine(Made, id)).FullName);
    }

    private string? FolderOf(string id) =>
        id.Length == 32 && id.All(char.IsAsciiHexDigitLower) ? Path.Combine(Made, id) : null;

    private string? MadeFile(string id) =>
        FolderOf(id) is { } folder && Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder).SingleOrDefault()
            : null;

    /// <summary>A text table saved with a UTF-8 byte order mark, as a spreadsheet saves one, read without it.</summary>
    private static async Task SkipByteOrderMark(Stream body, Stream file, CancellationToken cancellationToken)
    {
        var start = new byte[3];
        var read = 0;
        int count;
        while (read < start.Length && (count = await body.ReadAsync(start.AsMemory(read), cancellationToken)) > 0)
        {
            read += count;
        }

        if (!start.AsSpan(0, read).SequenceEqual((ReadOnlySpan<byte>)[0xEF, 0xBB, 0xBF]))
        {
            await file.WriteAsync(start.AsMemory(0, read), cancellationToken);
        }
    }

    private static string NameOf(string? name)
    {
        var file = Path.GetFileName(name?.Trim() ?? "");
        return file.Length == 0 || file.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetExtension(file).Length < 2
            ? throw new ArgumentException("The file needs a name with its extension, such as words.tsv or words.essx.")
            : file;
    }
}

internal static class EssxConversionEndpoints
{
    public static void MapEssxConversion(this RouteGroupBuilder routes)
    {
        // The table as the request's body, as a dropped .essx file is sent: no form, no field names.
        routes.MapPost("/essx/convert", async (string? name, bool? compress, HttpContext context, EssxConverter converter, CancellationToken cancellationToken) =>
        {
            if (context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } limit)
            {
                limit.MaxRequestBodySize = null;
            }

            try
            {
                return Results.Ok(await converter.Upload(name, compress == true, context.Request.Body, cancellationToken));
            }
            catch (Exception exception) when (exception is ProductFormatException or FormatException or ArgumentException)
            {
                return Results.UnprocessableEntity(new ProblemResponse(exception.Message));
            }
        });

        routes.MapPost("/essx/files/{id}/convert", (string id, string? table, EssxConverter converter) =>
        {
            try
            {
                return converter.FromOpen(id, table) is { } view
                    ? Results.Ok(view)
                    : Results.NotFound(new ProblemResponse("The file is no longer open; open it again."));
            }
            catch (Exception exception) when (exception is ProductFormatException or FormatException or ArgumentException)
            {
                return Results.UnprocessableEntity(new ProblemResponse(exception.Message));
            }
        });

        routes.MapGet("/essx/converted/{id}", (string id, EssxConverter converter) =>
            converter.Download(id) is { } file
                ? Results.File(file.Path, file.ContentType, file.Name, enableRangeProcessing: true)
                : Results.NotFound(new ProblemResponse("The converted file is gone; convert it again.")));

        routes.MapPost("/essx/converted/{id}/open", (string id, EssxConverter converter) =>
        {
            try
            {
                return converter.Open(id) is { } view
                    ? Results.Ok(view)
                    : Results.NotFound(new ProblemResponse("There is no converted .essx file by that name; convert it again."));
            }
            catch (Exception exception) when (exception is ProductFormatException or FormatException)
            {
                return Results.UnprocessableEntity(new ProblemResponse(exception.Message));
            }
        });

        routes.MapDelete("/essx/converted/{id}", (string id, EssxConverter converter) =>
        {
            try
            {
                return converter.Forget(id) ? Results.NoContent() : Results.NotFound(new ProblemResponse("The converted file is gone."));
            }
            catch (IOException)
            {
                return Results.Conflict(new ProblemResponse("The converted file is still being downloaded; it goes within a day."));
            }
        });
    }
}
