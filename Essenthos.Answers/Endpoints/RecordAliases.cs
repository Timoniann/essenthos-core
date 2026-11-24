using System.Text;
using System.Text.RegularExpressions;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Microsoft.EntityFrameworkCore;

namespace Essenthos.Core.Endpoints;

/// <summary>
/// The record a page address names by another of the record's names: <c>/people/abraham</c> for the
/// record <c>abram</c>, <c>/people/cephas</c> for <c>simon</c>, <c>/places/tower-of-the-hundred</c>
/// for <c>towerofthehundred</c>.
///
/// <para>
/// People and the answers of search assistants guess a page's address from the name they know. A
/// guess is matched on its letters and digits alone, against the record's own name, its address, and
/// the other names the record holds as names (its proper names, the gazetteer's spellings of a place),
/// never its titles or descriptions. Only a name one record of the address's kind holds leads there;
/// a name two records share (Edom the man and Edom the land) leads nowhere, and the address stays
/// not found.
/// </para>
/// </summary>
internal static class RecordAliases
{
    /// <summary>The kinds of <c>entity_name</c> row that are a name of the record, and not something said of it.</summary>
    private static readonly string[] NameKinds = ["proper name", "name", "spelling"];

    private const string NotLetterOrDigit = "[^[:alnum:]]";

    /// <summary>The address of the one record of this kind the slug names by another of its names, or null.</summary>
    public static async Task<string?> Find(AppDbContext db, string slug, EntityKind kind, CancellationToken cancellationToken)
    {
        var key = Key(slug);
        if (key.Length == 0 || WithdrawnAddresses.IsWithdrawn(slug))
        {
            return null;
        }

        var holders = await db.Entities
            .Where(e => e.Kind == kind && (
                e.Slug.Replace("-", "") == key
                || Regex.Replace(e.Name, NotLetterOrDigit, "").ToLower() == key
                || db.EntityNames.Any(n => n.EntityId == e.Id && NameKinds.Contains(n.Kind)
                    && Regex.Replace(n.Label, NotLetterOrDigit, "").ToLower() == key)))
            .Select(e => e.Slug)
            .Take(2)
            .ToListAsync(cancellationToken);
        return holders.Count == 1 ? holders[0] : null;
    }

    /// <summary>A name or an address as it is compared: its letters and digits, in lower case.</summary>
    public static string Key(string name)
    {
        var key = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            if (char.IsLetterOrDigit(c))
            {
                key.Append(char.ToLowerInvariant(c));
            }
        }

        return key.ToString();
    }
}
