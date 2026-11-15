namespace Essenthos.Core.Products;

/// <summary>
/// A file written beside its final name and moved into place whole, so that a reader sees either
/// the previous file or the new one and never a half of either. Disposing without
/// <see cref="Commit"/> throws the writing away and leaves what was there; a failed producer, a
/// killed build or an exception in a writer keeps the last good product on disk.
/// </summary>
public sealed class AtomicFile : IDisposable
{
    private const string Suffix = ".tmp";

    private readonly string _temporary;
    private bool _committed;

    private AtomicFile(string path, string temporary, FileStream stream)
    {
        Path = path;
        _temporary = temporary;
        Stream = stream;
    }

    public string Path { get; }

    public FileStream Stream { get; }

    /// <summary>Opens the temporary file, creating the folder the final one goes in.</summary>
    public static AtomicFile Create(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var folder = System.IO.Path.GetDirectoryName(full);
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        var temporary = $"{full}.{Guid.NewGuid():N}{Suffix}";
        var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None, 1 << 16);
        return new AtomicFile(full, temporary, stream);
    }

    /// <summary>Flushes to disk and moves the file over the final name.</summary>
    public void Commit()
    {
        if (_committed)
        {
            return;
        }

        Stream.Flush(flushToDisk: true);
        Stream.Dispose();
        Replace(_temporary, Path);
        _committed = true;
    }

    /// <summary>
    /// Moves <paramref name="from"/> over <paramref name="to"/>. Windows refuses the move while another
    /// handle holds the target open (a reader of the previous file, another process saving it, a virus
    /// scanner or indexer), so a refusal is tried again for about two seconds before it is thrown.
    /// </summary>
    internal static void Replace(string from, string to)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite: true);
                return;
            }
            catch (Exception e) when (attempt < RetryDelaysMs.Length && e is IOException or UnauthorizedAccessException && File.Exists(from))
            {
                Thread.Sleep(RetryDelaysMs[attempt]);
            }
        }
    }

    private static readonly int[] RetryDelaysMs = [10, 20, 40, 80, 160, 320, 640, 640];

    public void Dispose()
    {
        if (_committed)
        {
            return;
        }

        Stream.Dispose();
        try
        {
            File.Delete(_temporary);
        }
        catch (IOException)
        {
            // Left behind by a sharing violation; the suffix names it for a sweep, and nothing reads it.
        }
    }

    public static void WriteAllText(string path, string text)
    {
        using var file = Create(path);
        using (var writer = new StreamWriter(file.Stream, Utf8.NoBom, 1 << 16, leaveOpen: true))
        {
            writer.NewLine = "\n";
            writer.Write(text);
        }

        file.Commit();
    }

    public static void WriteAllBytes(string path, ReadOnlySpan<byte> bytes)
    {
        using var file = Create(path);
        file.Stream.Write(bytes);
        file.Commit();
    }
}

/// <summary>Every product is UTF-8 without a byte-order mark, which <c>COPY</c>, git and grep all read as plain text.</summary>
public static class Utf8
{
    public static readonly System.Text.UTF8Encoding NoBom = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
}
