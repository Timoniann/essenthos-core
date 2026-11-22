using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Database.Entities;

/// <summary>
/// A constant of one corpus release, counted once by the serving build so the API reads it instead of counting it at every
/// start: what each text holds and is linked to, what each dataset accounts for, what the Chapter Context weighs against.
/// Facts by their key, as JSON; <see cref="Stamp"/> names the code that counted them, and an API whose own stamp differs
/// counts for itself.
/// </summary>
[PrimaryKey(nameof(Key))]
public class ReleaseFact
{
    /// <summary>Which fact: <c>texts</c>, <c>datasets</c>, <c>context-weights</c>.</summary>
    public required string Key { get; set; }

    /// <summary>The version of the counting code and the declarations it read.</summary>
    public required string Stamp { get; set; }

    /// <summary>The fact, as JSON.</summary>
    public required string Body { get; set; }
}
