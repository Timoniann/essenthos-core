using System.Collections.Concurrent;
using Essenthos.Core.Conversion;
using Essenthos.Core.Corpus;
using Essenthos.Core.Database;
using Essenthos.Core.Database.Entities.Enums;
using Essenthos.Core.Loading;
using Essenthos.Core.Loading.Encyclopedia;
using Essenthos.Core.Loading.Frame;
using Essenthos.Core.Names;
using Essenthos.Core.Products;

namespace Essenthos.Core.Records;

/// <summary>
/// The verses our words and relationships put a record in, <c>records/derived/verses</c>, from the names the projection
/// puts on the words of every held text (<c>names/projected</c>), where each word's verse stands (<c>texts/*/frame</c>) and
/// the relationships (<c>records/facts/relationships</c>). It is what the old load's passes left in <c>entity_verse</c> under
/// their two sources, made again from files on every change of a name:
/// <list type="bullet">
/// <item>every record a word names, as the reader is told it (<see cref="Annotations.Settle"/>), at the primary canonical
/// address of the word's verse, once however many words and texts name it there: a person, a place, a title, an object or an
/// observance under <see cref="OwnReferences"/> with no label, a people under <see cref="PeopleReferences"/> with its name as
/// the label. A verse the frame stands at several addresses (a psalm's title printed in its first verse) lists the record
/// at the one of them where a verse standing at a single address names it, else at its primary one, so the title and the
/// verse after it are not both listed for a name only one of them holds. A name carried from a word standing at a psalm's
/// title stands at the title too, where the carrying text's frame does not know its verse holds the title;</item>
/// <item>withheld where every word naming a man in the verse is the name after <em>the sons of</em> that the owner's ruling
/// reads as the people beside a word naming the people in the same verse of the same text (<see cref="Annotations.SonsOf"/>),
/// or, for a name carried from another text, of the verse of the word it was carried from: a translation whose word for
/// <em>sons</em> the projection did not pair carries the man alone, and the ruling was read beside the people where it
/// was stated. A man the ruling names with no people beside him in his text's verse also stands apart where another
/// text names his own people (the people whose origin he is) under the ruling at the same canonical verse: a settled
/// Vulgate or Ge'ez row names the man on the name and nothing on its word for <em>sons</em>;</item>
/// <item>for a record no other verse lists, the verses its relationships were read from (<see cref="RelationshipVerseLoader"/>),
/// naming it where a word of the verse carries it in any text.</item>
/// </list>
/// A dataset's verse lists (a gazetteer's, BibleData's) are never a record's verses: a place is in the verses our words or
/// its relationships put it in.
/// A term a relationship lists is named where a word of the verse, in any text, carries one of its Strong numbers: no word
/// is annotated to a term.
/// A base reading beside a differs is the base text's name, not the word's, and puts no verse on a page.
/// </summary>
internal static class RecordVerses
{
    public const string Product = "records/derived/verses";

    public const string Producer = "forge records-verses";

    public const string Version = "8";

    public const string VersionNote = "verses-version";

    private const string OwnReferences = "Essenthos, from the words this corpus annotates to the person or the place they name";

    private const string PeopleReferences = "Essenthos, from the words this corpus annotates to the people";

    private const string RelationshipReferences = RelationshipVerseLoader.Source;

    /// <summary>The kinds whose verses are read off the words; the words for God are read off their Strong numbers elsewhere.</summary>
    private static readonly IReadOnlySet<EntityKind> Named =
        new HashSet<EntityKind> { EntityKind.Person, EntityKind.Place, EntityKind.Title, EntityKind.Object, EntityKind.Observance, EntityKind.Being };

    /// <summary>The files besides the names and the frames that the verses are read from.</summary>
    private static readonly string[] Facts =
    [
        "records/records", "records/names", "records/facts/titles", "records/facts/relationships", "records/facts/verses",
    ];

    public sealed record Outcome(bool Written, long Rows, long Records, long Names, long Withheld, long FromRelationships);

    /// <summary>A name on a word, as the projection or a database's export gives it.</summary>
    /// <param name="Role">Null where the database's row predates roles.</param>
    /// <param name="Through">The word of another text it was carried from, with that text (<c>BHSA 19.3:1.3</c>); null when not carried.</param>
    internal sealed record WordName(WordKey Own, string Record, string? Role, string? Through, string Method, double? Confidence, string Source)
    {
        public bool Carried => Through is not null;
    }

    /// <summary>Writes <c>records/derived/verses</c> when an input moved or the rule's version did, or when forced.</summary>
    public static Outcome Write(ProductStore store, bool force)
    {
        var texts = Texts(store, "projected");
        string[] read =
        [
            .. texts.Select(NameProducts.Projected),
            .. texts.Select(FrameFile.Product),
            .. texts.Select(TextWord.ProductOf),
            .. Facts,
        ];
        var inputs = store.Inputs(read.Select(store.Find).OfType<string>());
        if (!force && !NeedsBuild(store, inputs))
        {
            return new Outcome(false, 0, 0, 0, 0, 0);
        }

        var (rows, outcome) = Derive(store, texts, text =>
        {
            using var reader = store.Open(NameProducts.Projected(text), ProjectedName.Schema(text));
            return [.. reader.Rows().Where(n => NameOrigins.IsOwn(n.Origin))
                .Select(n => new WordName(n.Own, n.Record, n.Role, n.Through, n.Method, n.Confidence, n.Source))];
        });
        var file = RecordFiles.All.Single(f => f.Product == Product);
        using var writer = store.Create(Product, file.Kind, file.Schema(), ProductHeader.DescribeProducer(Producer), inputs,
            header =>
            {
                header.Notes.Add(new(VersionNote, Version));
                header.Comments.Add(file.About!);
            });
        writer.WriteAll(rows);
        writer.Complete();
        return outcome with { Written = true };
    }

    private static (List<string?[]> Rows, Outcome Outcome) Derive(ProductStore store, List<string> texts, Func<string, List<WordName>> names)
    {
        var records = Records(store);
        var ids = records.Select((r, i) => (r.Slug, i)).ToDictionary(p => p.Slug, p => p.i, StringComparer.Ordinal);
        var peoplesOf = records.Select((r, i) => (r, i))
            .Where(p => p.r.Kind == EntityKind.People && p.r.Origin is { } origin && ids.ContainsKey(origin))
            .GroupBy(p => ids[p.r.Origin!], p => p.i)
            .ToDictionary(g => g.Key, g => g.ToArray());
        var bearers = Read(store, "records/facts/titles")
            .Where(r => ids.ContainsKey(r[0]!) && ids.ContainsKey(r[1]!))
            .Select(r => (ids[r[0]!], ids[r[1]!]))
            .ToHashSet();

        var relationshipRows = Read(store, "records/facts/relationships")
            .Where(r => r[3] is not null && !r[8]!.StartsWith(ShownVerses.Witness, StringComparison.Ordinal))
            .ToList();
        var terms = Terms(store, records, ids, relationshipRows.SelectMany(r => new[] { r[0]!, r[2]! }));

        // (record, canonical verse) => whether every word naming the record there stands apart for its people.
        var named = new ConcurrentDictionary<(int Record, VerseKey Verse), bool>();
        var anyName = new ConcurrentDictionary<(int Record, VerseKey Verse), byte>();
        var apart = new ConcurrentDictionary<int, byte>();
        var spanning = new ConcurrentBag<(int Record, IReadOnlyList<VerseKey> Addresses, bool Apart)>();

        // Each text's own verses where the ruling names a people, every canonical verse where it names each people, and the
        // men the ruling names in a verse of a text where it names no people, with the word they were carried from if
        // they were: they stand apart where that word's verse holds a people, or where any text names their own people
        // at their verse, settled once every text is read.
        var peopleVersesOf = new ConcurrentDictionary<string, HashSet<VerseKey>>(StringComparer.Ordinal);
        var peopleAt = new ConcurrentDictionary<(int People, VerseKey Verse), byte>();
        var standingAlone = new ConcurrentBag<(int Record, IReadOnlyList<VerseKey> Addresses, (string Text, VerseKey Verse)? From)>();
        var frames = new ConcurrentDictionary<string, Dictionary<VerseKey, IReadOnlyList<VerseKey>>>(StringComparer.Ordinal);
        IReadOnlyList<VerseKey> AddressesIn(string text, VerseKey own) =>
            frames.GetOrAdd(text, t => Frame(store, t)).TryGetValue(own, out var addresses) ? addresses : [FrameFile.AtOwn(own)];
        long read = 0;
        Parallel.ForEach(texts, new ParallelOptions { MaxDegreeOfParallelism = 8 }, text =>
        {
            IReadOnlyList<VerseKey> Addresses(VerseKey own) => AddressesIn(text, own);
            VerseKey Placed(VerseKey own) => Addresses(own)[0];

            // The title a carried name's word stands at, where the name's verse is that psalm's first and does not stand at it.
            VerseKey? TitleOf(WordName name)
            {
                var own = Addresses(name.Own.Verse);
                if (own[0].Verse != 1 || name.Through is not { } through)
                {
                    return null;
                }

                var space = through.IndexOf(' ');
                if (space < 1)
                {
                    return null;
                }

                var baseText = through[..space];
                var title = AddressesIn(baseText, WordKey.Parse(through[(space + 1)..], baseText).Verse)[0];
                return title.Verse == CanonicalReference.TitleVerse && own[0].Book == title.Book && own[0].Chapter == title.Chapter
                    && !own.Contains(title)
                        ? title
                        : null;
            }

            var rows = names(text).Where(n => ids.ContainsKey(n.Record)).ToList();
            Interlocked.Add(ref read, rows.Count);
            var words = new Dictionary<WordKey, long>();
            var keys = new List<WordKey>();
            var claimed = new List<Annotations.Claimed>(rows.Count);
            var sonsOf = new HashSet<(long Word, int Record)>();
            var carriedFrom = new Dictionary<(long Word, int Record), (string Text, VerseKey Verse)>();
            var titles = new Dictionary<(long Word, int Record), VerseKey>();
            foreach (var row in rows)
            {
                if (!words.TryGetValue(row.Own, out var word))
                {
                    words[row.Own] = word = keys.Count;
                    keys.Add(row.Own);
                }

                var id = ids[row.Record];
                var record = records[id];
                claimed.Add(new Annotations.Claimed(
                    word, EnumSpelling.ToLinkMethod(row.Method), row.Confidence, row.Source, row.Carried ? $"through {row.Through}" : null,
                    record.Kind, record.Slug, record.Slug)
                {
                    EntityId = id,
                    Role = row.Role,
                });
                anyName.TryAdd((id, Placed(row.Own.Verse)), 0);
                if (TitleOf(row) is { } title)
                {
                    titles[(word, id)] = title;
                }

                if (row.Source == Annotations.SonsOf)
                {
                    sonsOf.Add((word, id));
                    if (row.Through is { } from && from.IndexOf(' ') is var gap and > 0)
                    {
                        var fromText = from[..gap];
                        carriedFrom[(word, id)] = (fromText, WordKey.Parse(from[(gap + 1)..], fromText).Verse);
                    }
                }
            }

            if (terms.Count > 0 && store.Exists(TextWord.ProductOf(text)))
            {
                using var numbered = store.Open(TextWord.ProductOf(text), TextWord.Schema(text));
                foreach (var numberedWord in numbered.Rows())
                {
                    if (numberedWord.Strong is { } strong && terms.TryGetValue(strong, out var bearing))
                    {
                        foreach (var term in bearing)
                        {
                            anyName.TryAdd((term, Placed(numberedWord.Key.Verse)), 0);
                        }
                    }
                }
            }

            var settled = Annotations.Settle(claimed, bearers);

            // A man named by the ruling on 'the sons of' in a verse of this text where the ruling names a people too.
            var ruled = settled.SelectMany(w => w.Value.Where(c => sonsOf.Contains((w.Key, c.EntityId))).Select(c => (Word: w.Key, c.EntityId, c.Kind)))
                .ToList();
            var peopleVerses = ruled.Where(r => r.Kind == EntityKind.People).Select(r => keys[(int)r.Word].Verse).ToHashSet();
            peopleVersesOf[text] = peopleVerses;
            foreach (var people in ruled.Where(r => r.Kind == EntityKind.People))
            {
                foreach (var address in Addresses(keys[(int)people.Word].Verse))
                {
                    peopleAt.TryAdd((people.EntityId, address), 0);
                }
            }

            var standsApart = ruled.Where(r => r.Kind == EntityKind.Person && peopleVerses.Contains(keys[(int)r.Word].Verse))
                .Select(r => (r.Word, r.EntityId)).ToHashSet();
            var alone = ruled.Where(r => r.Kind == EntityKind.Person && !peopleVerses.Contains(keys[(int)r.Word].Verse))
                .Select(r => (r.Word, r.EntityId)).ToHashSet();
            foreach (var (_, record) in standsApart)
            {
                apart.TryAdd(record, 0);
            }

            foreach (var (word, answers) in settled)
            {
                var verseAddresses = Addresses(keys[(int)word].Verse);
                foreach (var answer in answers)
                {
                    IReadOnlyList<VerseKey> addresses =
                        titles.TryGetValue((word, answer.EntityId), out var title) ? [.. verseAddresses, title] : verseAddresses;
                    // A man standing alone counts as apart until every text is read.
                    var isApart = standsApart.Contains((word, answer.EntityId));
                    if (alone.Contains((word, answer.EntityId)))
                    {
                        standingAlone.Add((answer.EntityId, addresses,
                            carriedFrom.TryGetValue((word, answer.EntityId), out var from) ? from : null));
                        isApart = true;
                    }

                    if (addresses.Count == 1)
                    {
                        named.AddOrUpdate((answer.EntityId, addresses[0]), isApart, (_, all) => all && isApart);
                    }
                    else
                    {
                        spanning.Add((answer.EntityId, addresses, isApart));
                    }
                }
            }
        });

        bool HisPeopleAt(int man, IReadOnlyList<VerseKey> addresses) =>
            peoplesOf.TryGetValue(man, out var peoples)
            && addresses.Any(address => peoples.Any(people => peopleAt.ContainsKey((people, address))));

        foreach (var (id, addresses, from) in standingAlone)
        {
            if ((from is { } carried && peopleVersesOf.TryGetValue(carried.Text, out var verses) && verses.Contains(carried.Verse))
                || HisPeopleAt(id, addresses))
            {
                apart.TryAdd(id, 0);
            }
            else if (addresses.Count == 1)
            {
                named[(id, addresses[0])] = false;
            }
            else
            {
                spanning.Add((id, addresses, false));
            }
        }

        var single = named.Keys.ToHashSet();
        foreach (var (id, addresses, isApart) in spanning)
        {
            var verse = addresses.FirstOrDefault(address => single.Contains((id, address)), addresses[0]);
            named.AddOrUpdate((id, verse), isApart, (_, all) => all && isApart);
        }

        var rows = new List<string?[]>();
        long withheld = 0;
        foreach (var ((id, verse), allApart) in named)
        {
            var record = records[id];
            if (record.Kind == EntityKind.People)
            {
                rows.Add([record.Slug, verse.ToString(), record.Name, "t", "f", "f", PeopleReferences]);
            }
            else if (Named.Contains(record.Kind))
            {
                var held = allApart && apart.ContainsKey(id);
                withheld += held ? 1 : 0;
                rows.Add([record.Slug, verse.ToString(), null, "t", "f", held ? "t" : "f", OwnReferences]);
            }
        }

        string? Names(string slug, string verse) => anyName.ContainsKey((ids[slug], VerseKey.Parse(verse))) ? "t" : "f";

        // A record is listed where any verse a reader is shown lists it: ours read off the words, or a reading of ours by hand.
        var listed = rows.Select(r => r[0]!)
            .Concat(Read(store, "records/facts/verses").Select(r => r[0]!))
            .ToHashSet(StringComparer.Ordinal);
        var fromRelationships = relationshipRows
            .SelectMany(r => new[] { (Slug: r[0]!, Ref: r[3]!), (Slug: r[2]!, Ref: r[3]!) })
            .Where(r => ids.ContainsKey(r.Slug) && !listed.Contains(r.Slug))
            .Distinct()
            .Select(r => new string?[]
            {
                r.Slug, r.Ref, null, Names(r.Slug, r.Ref), "f", "f", RelationshipReferences,
            })
            .ToList();
        rows.AddRange(fromRelationships);

        var file = RecordFiles.All.Single(f => f.Product == Product);
        var sorted = file.Sorted(rows, file.Order(file.Export));
        return (sorted, new Outcome(false, sorted.Count, sorted.Select(r => r[0]).Distinct().Count(), read, withheld, fromRelationships.Count));
    }

    /// <summary>The Strong numbers of the terms among <paramref name="listed"/>, each with the terms that bear it.</summary>
    private static Dictionary<string, List<int>> Terms(
        ProductStore store, List<Record> records, Dictionary<string, int> ids, IEnumerable<string> listed)
    {
        var wanted = listed.Where(slug => ids.TryGetValue(slug, out var id) && records[id].Kind == EntityKind.Term)
            .ToHashSet(StringComparer.Ordinal);
        if (wanted.Count == 0)
        {
            return [];
        }

        return Read(store, "records/names")
            .Where(r => wanted.Contains(r[0]!))
            .SelectMany(r => new[] { r[8], r[9] }.OfType<string>().Select(strong => (Strong: strong, Term: ids[r[0]!])))
            .Distinct()
            .GroupBy(p => p.Strong, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Term).ToList(), StringComparer.Ordinal);
    }

    private static bool NeedsBuild(ProductStore store, IReadOnlyList<ProductInput> inputs)
    {
        if (store.NeedsBuild(Product, ProductKind.Machine, inputs))
        {
            return true;
        }

        using var reader = store.Open(Product);
        return reader.Header?.Notes.LastOrDefault(note => note.Key == VersionNote).Value != Version;
    }

    /// <summary>The texts with a file in <c>names/&lt;folder&gt;/</c>, in name order.</summary>
    private static List<string> Texts(ProductStore store, string folder)
    {
        var path = Path.Combine(store.ResourcesPath, ProductStore.EssenthosFolder, NameProducts.Folder, folder);
        return Directory.Exists(path)
            ? [.. Directory.GetFiles(path).Where(file => !Path.GetFileName(file).StartsWith('.'))
                .Select(Path.GetFileNameWithoutExtension).OfType<string>().Distinct().Order(StringComparer.Ordinal)]
            : [];
    }

    /// <summary>The frame addresses of every verse a text's frame places away from its own address, the primary one first.</summary>
    private static Dictionary<VerseKey, IReadOnlyList<VerseKey>> Frame(ProductStore store, string text)
    {
        var frame = new Dictionary<VerseKey, IReadOnlyList<VerseKey>>();
        if (!store.Exists(FrameFile.Product(text)))
        {
            return frame;
        }

        using var reader = store.Open(FrameFile.Product(text), FrameRow.Schema);
        foreach (var row in reader.Rows())
        {
            foreach (var (own, canonical) in row.Verses())
            {
                frame[own] = canonical;
            }
        }

        return frame;
    }

    /// <param name="Origin">The man a people came from.</param>
    private sealed record Record(string Slug, EntityKind Kind, string Name, string? Origin);

    private static List<Record> Records(ProductStore store) =>
        [.. Read(store, "records/records").Select(r => new Record(r[0]!, EnumSpelling.ToEntityKind(r[1]!), r[4]!, r[9]))];

    private static List<string?[]> Read(ProductStore store, string product)
    {
        using var reader = store.Open(product, RecordFiles.All.Single(f => f.Product == product).Schema());
        return [.. reader.ReadAll()];
    }
}
