using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Loading;

/// <summary>A declaration field a text takes whole from its definition: nothing after its first load writes it.</summary>
internal enum TextField
{
    Name,
    About,
    Citation,
    Edition,
    RightsHolder,
}

/// <summary>Fields of one text that are set again from its definition where they differ.</summary>
internal sealed record TextFieldRepair(string Slug, params TextField[] Fields);

/// <summary>
/// A passage of a text's rights note replaced where it stands. The note cannot be set whole: the steps
/// after a text's first load append their own sentences to it.
/// </summary>
/// <param name="Was">What the note said; null for a note that was empty, which then takes the definition's.</param>
/// <param name="Now">What it says instead; empty to take the passage out.</param>
internal sealed record TextNoteRepair(string Slug, string? Was, string Now = "");

/// <summary>A credit on a text's part sources replaced, or added where <paramref name="Was"/> is null.</summary>
internal sealed record TextPartRepair(string Slug, TextPartSource? Was, TextPartSource Now);

/// <param name="Texts">Texts whose declaration changed.</param>
/// <param name="Changes">Fields, passages and credits changed among them.</param>
internal sealed record TextDeclarationOutcome(int Texts, int Changes)
{
    public override string ToString() => Changes == 0
        ? "Every text's declaration already says what its definition says"
        : $"{Changes} declarations brought in line with their definitions, on {Texts} texts";
}

/// <summary>
/// What a text says about itself — its name, citation, rights note, edition, holder and part sources —
/// brought in line with its definition on a corpus that loaded the text before the definition changed.
/// A text is declared once, when its words are first written, so a corrected definition reaches a
/// corpus already holding the text only through here. A cold load writes the definitions as they are
/// and finds nothing to do.
/// </summary>
internal sealed class TextDeclarationLoader(AppDbContext db, ILogger<TextDeclarationLoader> logger)
{
    public async Task<TextDeclarationOutcome> Load(CancellationToken cancellationToken = default)
    {
        var definitions = TextCorpus.Definitions.ToDictionary(d => d.Slug, StringComparer.OrdinalIgnoreCase);
        var slugs = TextDeclarations.Fields.Select(r => r.Slug)
            .Concat(TextDeclarations.Notes.Select(r => r.Slug))
            .Concat(TextDeclarations.Parts.Select(r => r.Slug))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var texts = await db.Texts.Where(t => slugs.Contains(t.Slug)).ToListAsync(cancellationToken);

        var changes = 0;
        var changed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var text in texts)
        {
            var before = changes;
            var definition = definitions[text.Slug];
            foreach (var repair in TextDeclarations.Fields.Where(r => Same(r.Slug, text.Slug)))
            {
                changes += repair.Fields.Count(field => Set(text, definition, field));
            }

            foreach (var repair in TextDeclarations.Notes.Where(r => Same(r.Slug, text.Slug)))
            {
                if (Repaired(text.RightsNote, definition.RightsNote, repair) is { } note)
                {
                    text.RightsNote = note;
                    changes++;
                }
                else if (repair.Was is not null && repair.Now.Length > 0
                         && text.RightsNote?.Contains(repair.Now, StringComparison.Ordinal) != true)
                {
                    logger.LogWarning(
                        "{Slug}'s rights note holds neither the passage a repair replaces nor its replacement; " +
                        "something else has written into it. Compare it with the definition by hand.", text.Slug);
                }
            }

            foreach (var repair in TextDeclarations.Parts.Where(r => Same(r.Slug, text.Slug)))
            {
                if (Repaired(TextPartSources.Of(text), repair) is { } parts)
                {
                    TextPartSources.Replace(text, parts);
                    changes++;
                }
            }

            if (changes > before)
            {
                changed.Add(text.Slug);
            }
        }

        if (changes > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        var outcome = new TextDeclarationOutcome(changed.Count, changes);
        logger.LogInformation("{Outcome}{Texts}", outcome, changed.Count == 0 ? "" : $": {string.Join(", ", changed.Order())}");
        return outcome;
    }

    /// <summary>The note with the repair made, or null where it has nothing to do.</summary>
    internal static string? Repaired(string? note, string? defined, TextNoteRepair repair)
    {
        if (repair.Was is null)
        {
            return string.IsNullOrEmpty(note) && !string.IsNullOrEmpty(defined) ? defined : null;
        }

        if (note is null || !note.Contains(repair.Was, StringComparison.Ordinal))
        {
            return null;
        }

        return note.Replace(repair.Was, repair.Now, StringComparison.Ordinal).Trim();
    }

    /// <summary>The credits with the repair made, or null where it has nothing to do.</summary>
    internal static IReadOnlyList<TextPartSource>? Repaired(IReadOnlyList<TextPartSource> parts, TextPartRepair repair)
    {
        if (parts.Contains(repair.Now))
        {
            return repair.Was is not null && parts.Contains(repair.Was)
                ? [.. parts.Where(part => part != repair.Was)]
                : null;
        }

        if (repair.Was is null)
        {
            return [.. parts, repair.Now];
        }

        return parts.Contains(repair.Was) ? [.. parts.Select(part => part == repair.Was ? repair.Now : part)] : null;
    }

    private static bool Set(Text text, TextDefinition definition, TextField field)
    {
        var (stored, defined) = field switch
        {
            TextField.Name => (text.Name, definition.Name),
            TextField.About => (text.About, definition.About),
            TextField.Citation => (text.Citation, definition.Citation),
            TextField.Edition => (text.Edition, definition.Edition),
            TextField.RightsHolder => (text.RightsHolder, definition.RightsHolder),
            _ => throw new ArgumentOutOfRangeException(nameof(field), field, null),
        };

        if (string.Equals(stored, defined, StringComparison.Ordinal))
        {
            return false;
        }

        switch (field)
        {
            case TextField.Name:
                text.Name = defined!;
                break;
            case TextField.About:
                text.About = defined;
                break;
            case TextField.Citation:
                text.Citation = defined;
                break;
            case TextField.Edition:
                text.Edition = defined;
                break;
            case TextField.RightsHolder:
                text.RightsHolder = defined;
                break;
        }

        return true;
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
