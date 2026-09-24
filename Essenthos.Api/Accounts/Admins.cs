using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Accounts;

/// <summary>
/// Who may use the admin area: an account another admin made one, or an account with a verified
/// address the site's configuration names in <c>Accounts:BootstrapAdmins</c> — which is how the first
/// admin exists at all, and why nobody's address is written in the code.
///
/// Asked of the database on every admin request, one indexed lookup, and never cached: an admin whose
/// role is taken away stops being one on their next request, as a revoked session does.
/// </summary>
internal sealed class Admins(IReadOnlyList<string> configured)
{
    public const string ConfigurationKey = "Accounts:BootstrapAdmins";

    /// <summary>The configured addresses, lower-cased as <see cref="AccountEmail"/> keeps them.</summary>
    public IReadOnlyList<string> Configured { get; } = configured;

    /// <summary>
    /// The addresses under <see cref="ConfigurationKey"/>: a list (<c>:0</c>, <c>:1</c>, … — what user
    /// secrets and appsettings write) or one value separated by commas or semicolons (what an
    /// environment variable can hold). Anything without an <c>@</c> is not an address and is dropped.
    /// </summary>
    public static IReadOnlyList<string> Read(IConfiguration configuration)
    {
        var section = configuration.GetSection(ConfigurationKey);
        var values = section.GetChildren().Select(child => child.Value).Append(section.Value);
        return values
            .SelectMany(value => (value ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            .Select(address => address.ToLowerInvariant())
            .Where(address => address.Contains('@'))
            .Distinct()
            .ToList();
    }

    public Task<bool> IsAdmin(AccountsDbContext db, Guid account, CancellationToken cancellationToken) =>
        db.Accounts.AnyAsync(a => a.Id == account && (a.Admin ||
            db.AccountEmails.Any(e => e.AccountId == a.Id && Configured.Contains(e.Email))), cancellationToken);

    /// <summary>The accounts the configuration makes admins, among <paramref name="accounts"/>.</summary>
    public async Task<HashSet<Guid>> ConfiguredAmong(AccountsDbContext db, IReadOnlyCollection<Guid> accounts, CancellationToken cancellationToken) =>
        Configured.Count == 0
            ? []
            : (await db.AccountEmails
                .Where(e => accounts.Contains(e.AccountId) && Configured.Contains(e.Email))
                .Select(e => e.AccountId)
                .ToListAsync(cancellationToken)).ToHashSet();

    /// <summary>Every admin, whichever way they are one, by name — who a suggestion can be given to.</summary>
    public async Task<List<(Guid Id, string Name)>> All(AccountsDbContext db, CancellationToken cancellationToken) =>
        (await db.Accounts
            .Where(a => a.Admin || db.AccountEmails.Any(e => e.AccountId == a.Id && Configured.Contains(e.Email)))
            .OrderBy(a => a.DisplayName)
            .Select(a => new { a.Id, a.DisplayName })
            .ToListAsync(cancellationToken))
        .Select(a => (a.Id, a.DisplayName))
        .ToList();
}
