using System.ComponentModel.DataAnnotations;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// What one step of the load last ran on: a digest of its code, the resource files it reads and the
/// tables it read as they stood when it started, and a digest of what it left in the tables it wrote.
/// A step whose inputs digest the same again would only write what is already there, so the load
/// skips it.
///
/// <para>
/// <see cref="Reads"/> are the tables the step's own statements named (and those of the Forge processes
/// it ran), and the ones it wrote, the last time it ran. No row, or no tables, means nothing records what
/// the step rests on, and the next load runs it.
/// </para>
/// </summary>
public class LoadStepMark
{
    [Key]
    public string Step { get; set; } = string.Empty;

    /// <summary>The digest of everything the step read, as it stood when the step started.</summary>
    public string InputDigest { get; set; } = string.Empty;

    /// <summary>The digest of the tables the step wrote, as it left them.</summary>
    public string OutputDigest { get; set; } = string.Empty;

    /// <summary>The tables the step read and wrote.</summary>
    public string[]? Reads { get; set; }

    /// <summary>
    /// The parts of <see cref="InputDigest"/> one by one (code, each file, each table), so a step that
    /// runs again can say which of them moved.
    /// </summary>
    public string Inputs { get; set; } = "{}";

    public DateTimeOffset At { get; set; }
}
