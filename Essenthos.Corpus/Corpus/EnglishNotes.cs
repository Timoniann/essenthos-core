using System.Security.Cryptography;
using System.Text;

namespace Essenthos.Core.Corpus;

/// <summary>
/// The digest a translation of a record's notes is keyed by: SHA-256 of the English notes exactly as
/// the record holds them, UTF-8, lower-case hex. The loader and the serving side ask the same
/// question with it, so a translation of notes since rewritten is shown by neither.
/// </summary>
public static class EnglishNotes
{
    public static string Digest(string english) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(english)));

    /// <summary>Whether a translation keyed by this digest renders what the record's notes now say.</summary>
    public static bool Renders(string? notes, string digest) =>
        !string.IsNullOrEmpty(notes) && string.Equals(Digest(notes), digest, StringComparison.OrdinalIgnoreCase);
}
