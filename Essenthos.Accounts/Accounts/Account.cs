namespace Essenthos.Core.Accounts;

/// <summary>
/// A reader who signed in. Everything a reader can set about themselves is here; how they prove who
/// they are is not — that is <see cref="Credential"/>, and an account has as many of those as
/// providers it has signed in with.
///
/// Nothing here refers to a row of the corpus. The corpus is replaced wholesale by every release
/// and renumbers every key when it is, so anything a reader writes about the text will address it
/// canonically (RUL-0185) — and an account has nothing to address yet.
/// </summary>
public class Account : IRevised
{
    public Guid Id { get; set; }

    /// <summary>What the reader is called on the site. Taken from the provider at first sign-in.</summary>
    public required string DisplayName { get; set; }

    public string? About { get; set; }

    /// <summary>The interface language the reader chose, or null for the browser's.</summary>
    public string? Locale { get; set; }

    /// <summary>
    /// The provider's picture, as a URL, from the first sign-in. Shown until the reader uploads one of
    /// their own, which is an <see cref="AccountPhoto"/>.
    /// </summary>
    public string? ProviderPhotoUrl { get; set; }

    /// <summary>
    /// Incremented with every upload, and part of the photo's URL, so the photo can be cached for ever
    /// and a new one is still seen at once.
    /// </summary>
    public int PhotoVersion { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Revision { get; set; }

    public List<Credential> Credentials { get; set; } = [];

    public override string ToString() => $"Account({Id}, {DisplayName})";
}

/// <summary>
/// One way of proving to be an account: a provider and the provider's own stable identifier for the
/// person. Never the email address — an address changes hands and a provider's subject does not.
/// </summary>
public class Credential
{
    public int Id { get; set; }

    public Guid AccountId { get; set; }

    public Account? Account { get; set; }

    /// <summary><c>google</c> or <c>github</c>. A later kind — an email link, a passkey — is a new value.</summary>
    public required string Provider { get; set; }

    public required string Subject { get; set; }

    /// <summary>
    /// The address the provider reported, kept so a reader can be told which of their accounts they
    /// signed in with. Not used to join accounts.
    /// </summary>
    public string? Email { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastUsedAt { get; set; }
}

/// <summary>
/// A signed-in device. The token is given to the device once and only its hash is kept, so a copy
/// of this table cannot sign anybody in.
///
/// A row rather than a signed cookie so that a session can be revoked — signing out on one device,
/// or all of them — and so the mobile app, which cannot hold a browser cookie, presents the same
/// token as a bearer.
/// </summary>
public class Session
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    public Account? Account { get; set; }

    /// <summary>SHA-256 of the token.</summary>
    public required byte[] TokenHash { get; set; }

    /// <summary>The user agent it was created from, trimmed, so a reader can tell their devices apart.</summary>
    public string? Device { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    public DateTimeOffset ExpiresAt { get; set; }
}

/// <summary>
/// A picture the reader uploaded. In the database rather than on a disk because it is small — the
/// upload is capped at a megabyte — and because then the nightly backup of this database is the
/// backup of the photos too, with nothing else to remember.
/// </summary>
public class AccountPhoto
{
    public Guid AccountId { get; set; }

    public required byte[] Content { get; set; }

    public required string ContentType { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// A row a second device has to be able to catch up on. Every save stamps it with the next value of
/// one sequence, so "what changed since revision N" is one indexed query — added now because
/// retrofitting it onto a year of rows is a backfill and a period in which nothing can sync.
/// </summary>
public interface IRevised
{
    long Revision { get; set; }

    DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>One data-protection key, as the framework's own XML.</summary>
public class DataProtectionKey
{
    public int Id { get; set; }

    public string? FriendlyName { get; set; }

    public required string Xml { get; set; }
}
