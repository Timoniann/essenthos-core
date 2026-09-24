namespace Essenthos.Core.Accounts;

/// <summary>
/// A reader who signed in. Everything a reader can set about themselves is here; how they prove who
/// they are is not — that is <see cref="Credential"/>, and an account has as many of those as
/// providers it has signed in with.
///
/// Nothing here refers to a row of the corpus. The corpus is replaced wholesale by every release
/// and renumbers every key when it is, so anything a reader writes about the text will address it
/// canonically — and an account has nothing to address yet.
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

    /// <summary>
    /// Granted by another admin in the admin area. An account can also be an admin because one of its
    /// verified addresses is in the site's configuration, which this does not record.
    /// </summary>
    public bool Admin { get; set; }

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
    /// The address the provider verified, kept so a reader can be told which of their accounts they
    /// signed in with; null when the provider verified none. Joining accounts goes through
    /// <see cref="AccountEmail"/>, never through this.
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

    /// <summary>The device this session was opened on, once the client has described it.</summary>
    public Guid? DeviceId { get; set; }
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

/// <summary>
/// A verified email address and the one account it belongs to. The key is the address, so the
/// database itself refuses a second account with it — "one address, one account" holds even for two
/// sign-ins racing each other, which a check in code alone would not.
///
/// Only addresses the provider says it verified are recorded. An unverified one proves nothing about
/// who holds it, and joining accounts on it would let anybody who typed somebody else's address into
/// a provider sign in as them.
/// </summary>
public class AccountEmail
{
    /// <summary>Lower-cased, since providers do not agree about case and people do not care.</summary>
    public required string Email { get; set; }

    public Guid AccountId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>
/// A phone, a laptop, a browser a reader uses. Each keeps its own settings and its own reading, as the
/// owner asked: a phone at night and a desk by day want different things, and "continue where I
/// stopped" means continuing what another device was reading.
///
/// A device is only ever recognised by the trace this site left on it: the id it was given the first
/// time, which the browser keeps. Nothing is guessed from what kind of device it looks like — two
/// laptops in the same browser look identical, and guessing would hand one the other's history. So a
/// sign-in in a browser that kept the id is the same device, and a sign-in anywhere else — including a
/// browser whose storage was cleared — is a new one. Kind, system, browser and model are kept only to
/// name the device to its reader.
/// </summary>
public class Device : IRevised
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary><c>mobile</c>, <c>tablet</c> or <c>desktop</c>.</summary>
    public required string Kind { get; set; }

    public required string Os { get; set; }

    public required string Browser { get; set; }

    /// <summary>What Android reports as the model, where the browser will say; null everywhere else.</summary>
    public string? Model { get; set; }

    /// <summary>
    /// The device's settings as the client keeps them — a JSON object the server stores and returns
    /// without reading, so a new setting in the client is not a migration here.
    /// </summary>
    public string? Settings { get; set; }

    /// <summary>When the settings last changed on the device, which decides whose copy is newer.</summary>
    public DateTimeOffset? SettingsChangedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeenAt { get; set; }

    /// <summary>
    /// When the device was signed out of — by signing out on it, or by its last session being ended from
    /// another — and null while it is signed in. Signing in again in the same browser clears it.
    /// </summary>
    public DateTimeOffset? SignedOutAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public long Revision { get; set; }
}

/// <summary>
/// A chapter a device showed, and when. Kept per device so each has its own history, so another device
/// can offer to continue it, and deleted with the device or the account.
///
/// A chapter, not a verse: what somebody reads, in what order, is the most sensitive thing this product
/// holds, and a chapter is enough to continue from.
/// </summary>
public class Reading
{
    public long Id { get; set; }

    public Guid AccountId { get; set; }

    public Guid DeviceId { get; set; }

    /// <summary>The book as the reader's address spells it — the canonical slug, never a corpus row id.</summary>
    public required string Book { get; set; }

    public int Chapter { get; set; }

    /// <summary>The texts that were open, comma-separated, so continuing opens the same split view.</summary>
    public required string Corpora { get; set; }

    public DateTimeOffset At { get; set; }
}
