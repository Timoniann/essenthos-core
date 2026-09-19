namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>Where an edition starts its text afresh before a word.</summary>
public enum TextBreak
{
    /// <summary>A new paragraph, or a blank line between stanzas.</summary>
    Paragraph,

    /// <summary>A new line within the paragraph: a line of poetry, an item of a list.</summary>
    Line,
}
