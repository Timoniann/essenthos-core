namespace Essenthos.Core.Accounts;

/// <summary>
/// Something a reader asks of the project: a text to add, an error in one, a record to correct, a
/// feature, a picture or a model, or anything else. Written by a signed-in reader, so an answer
/// reaches them; read and answered by the site's admins.
///
/// What each category asks for is kept as it was asked — <see cref="Fields"/> and <see cref="Links"/> —
/// rather than folded into one piece of prose, so the admin reading it sees the language, the edition
/// and the download link each where it belongs. A place in the text is a canonical address (a book's
/// slug, a chapter and a verse; a record's kind and slug), never a corpus row id.
/// </summary>
public class Suggestion
{
    public Guid Id { get; set; }

    public Guid AccountId { get; set; }

    /// <summary>One of <see cref="Categories"/>.</summary>
    public required string Category { get; set; }

    /// <summary>One of <see cref="Statuses"/>.</summary>
    public required string Status { get; set; }

    /// <summary>The main text: what is wrong, what is wanted — or, for a new text, the notes about it.</summary>
    public string? Body { get; set; }

    /// <summary>The category's own fields as a JSON object of strings, as the endpoint validated them.</summary>
    public required string Fields { get; set; }

    /// <summary>Every link the reader gave, each an absolute http or https address.</summary>
    public List<string> Links { get; set; } = [];

    /// <summary>The language of a proposed text, as the reader wrote it; what the admin list filters on.</summary>
    public string? Language { get; set; }

    public bool HasLink { get; set; }

    /// <summary>The interface language it was written in, so a reply can be written in it too.</summary>
    public string? Locale { get; set; }

    /// <summary>The admin who has taken it on, or null.</summary>
    public Guid? AssignedTo { get; set; }

    /// <summary>The body, the fields' values, the links, lower-cased in one string, for the admin search.</summary>
    public required string Search { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The last message of either side, which is what the admin list is ordered by.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>The last reply an admin wrote to the reader; newer than <see cref="AuthorSeenAt"/> is unread.</summary>
    public DateTimeOffset? LastReplyAt { get; set; }

    /// <summary>When its author last opened it.</summary>
    public DateTimeOffset AuthorSeenAt { get; set; }

    public static readonly IReadOnlyList<string> Categories = ["text", "error", "record", "feature", "picture", "other"];

    /// <summary>The first is what a new suggestion is.</summary>
    public static readonly IReadOnlyList<string> Statuses = ["new", "review", "done", "declined"];

    public override string ToString() => $"Suggestion({Id}, {Category}, {Status})";
}

/// <summary>
/// One message on a suggestion: the author's follow-up, an admin's reply, or an admin's note to the
/// other admins, which its author is never sent.
/// </summary>
public class SuggestionMessage
{
    public long Id { get; set; }

    public Guid SuggestionId { get; set; }

    /// <summary>Who wrote it; null once their account is gone, and the message stays unsigned.</summary>
    public Guid? AuthorId { get; set; }

    /// <summary>One of <see cref="Kinds"/>.</summary>
    public required string Kind { get; set; }

    public required string Body { get; set; }

    public DateTimeOffset At { get; set; }

    public const string Message = "message";
    public const string Reply = "reply";
    public const string Note = "note";

    public static readonly IReadOnlyList<string> Kinds = [Message, Reply, Note];
}

/// <summary>
/// Something an admin did, kept for as long as the database is: who, when, what, and on which
/// suggestion or account, with the value before and after. Written in the same save as the change it
/// records, so there is no change without its row.
///
/// The names are copied in rather than joined, so the row still says who it was after an account is
/// deleted.
/// </summary>
public class AdminAction
{
    public long Id { get; set; }

    public DateTimeOffset At { get; set; }

    public Guid? ActorId { get; set; }

    public required string ActorName { get; set; }

    /// <summary>One of the constants below.</summary>
    public required string Action { get; set; }

    public Guid? SuggestionId { get; set; }

    public Guid? AccountId { get; set; }

    /// <summary>The account acted on, by name, for the same reason as <see cref="ActorName"/>.</summary>
    public string? AccountName { get; set; }

    public string? Before { get; set; }

    public string? After { get; set; }

    public const string GrantAdmin = "grant-admin";
    public const string RevokeAdmin = "revoke-admin";
    public const string Replied = "reply";
    public const string Noted = "note";
    public const string StatusChanged = "status";
    public const string Assigned = "assign";
}
