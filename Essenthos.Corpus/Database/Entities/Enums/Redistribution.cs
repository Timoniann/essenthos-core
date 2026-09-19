namespace Essenthos.Core.Database.Entities.Enums;

/// <summary>
/// Whether the full text may be served publicly. Recorded per text so that "may we serve this?" is
/// a query rather than a memory.
/// </summary>
public enum Redistribution
{
    Unknown,
    PublicDomain,
    Permitted,
    PermittedWithAttribution,

    /// <summary>
    /// Attribution, and the ShareAlike clause: anything published as an *adaptation* of this text
    /// carries the same licence. It is a different answer from <see cref="PermittedWithAttribution"/>
    /// and the corpus could not say it until now — two texts were recorded under values that hid the
    /// obligation, one as plain attribution and one as public domain.
    ///
    /// What it does not mean is that the whole site becomes ShareAlike. Creative Commons binds an
    /// adaptation and explicitly not a collection, and a corpus that serves texts side by side is a
    /// collection — which was settled deliberately, after being got wrong twice.
    /// </summary>
    ShareAlike,

    NonCommercialOnly,
    Prohibited,
}
