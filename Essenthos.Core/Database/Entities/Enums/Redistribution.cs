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
    NonCommercialOnly,
    Prohibited,
}
