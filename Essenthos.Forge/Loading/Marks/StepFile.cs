namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// A file or folder a load step reads, named relative to Resources; a folder stands for every file under
/// it. <paramref name="ConfigurationKey"/> names the setting that can put it elsewhere, as the loaders
/// that read it allow; <paramref name="Built"/> means it is read from beside the Forge, where the build
/// copied it.
/// </summary>
internal sealed record StepFile(string Path, string? ConfigurationKey = null, bool Built = false)
{
    public static implicit operator StepFile(string path) => new(path);

    /// <summary>Where the file is read from in this run.</summary>
    public string Resolve(string resources, string contentRoot, IConfiguration configuration) =>
        ConfigurationKey is not null && configuration[ConfigurationKey] is { Length: > 0 } configured
            ? configured
            : System.IO.Path.Combine(Built ? contentRoot : resources, Path);

    public override string ToString() => Built ? $"(built) {Path}" : Path;
}
