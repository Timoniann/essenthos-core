namespace Essenthos.Core.Products;

/// <summary>
/// A lock held across processes for as long as a file beside the guarded one is open with no sharing:
/// two Forge processes on one <c>Resources/</c> (the lead's build and an agent's verb, or two stores in
/// one test run) take it in turn around a read-merge-write of a shared file. The lock is the open
/// handle, not the file's existence, so a process that dies releases it and the file left behind
/// blocks nothing.
/// </summary>
public sealed class FileLock : IDisposable
{
    private readonly FileStream _handle;

    private FileLock(FileStream handle)
    {
        _handle = handle;
    }

    /// <summary>Waits for the lock on <paramref name="path"/>; throws <see cref="TimeoutException"/> naming the file when it is held longer than <paramref name="timeout"/>.</summary>
    public static FileLock Acquire(string path, TimeSpan? timeout = null)
    {
        var folder = Path.GetDirectoryName(Path.GetFullPath(path));
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(1));
        var delay = 5;
        while (true)
        {
            try
            {
                return new FileLock(new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.None));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    throw new TimeoutException($"{path} stayed locked by another process past the wait; is a build still running on this Resources folder?", e);
                }

                Thread.Sleep(delay);
                delay = Math.Min(delay * 2, 100);
            }
        }
    }

    public void Dispose() => _handle.Dispose();
}
