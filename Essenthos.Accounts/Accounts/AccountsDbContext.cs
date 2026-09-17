using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Accounts;

/// <summary>
/// The database the API owns and writes: people, and everything a person keeps.
///
/// It is separate from the corpus because the two are written in opposite ways. The corpus is
/// written rarely and in bulk by a build, and a release replaces the whole of it; this is written a
/// row at a time by the API, every row belongs to somebody, and none of it can be rebuilt from a
/// source. That is also why the API opens the corpus read-only and this one owned.
///
/// Nothing here may reference a corpus row by its id. Those are sequence values and a release
/// renumbers all of them, so a note stored against one names a different verse afterwards, silently.
/// An anchor is a canonical address; for a word it is that address, the text, the ordinal in the
/// verse and the surface form as it then read.
///
/// Empty on purpose. The tables arrive with the feature that needs them, and this exists so that
/// the API has somewhere to write and the boundary is one the compiler checks.
/// </summary>
public class AccountsDbContext(DbContextOptions<AccountsDbContext> options) : DbContext(options);
