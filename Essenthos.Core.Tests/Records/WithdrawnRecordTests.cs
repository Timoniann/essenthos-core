using Essenthos.Core.Corpus;
using Essenthos.Core.Loading.Encyclopedia;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests.Records;

/// <summary>
/// The list of a dataset's records headed by a word that is no name, which the records load checks
/// <c>records/withdrawn</c> against and the API reads for the addresses it no longer serves.
/// </summary>
public sealed class WithdrawnRecordTests
{
    [Fact]
    public void TheMacbannitesAreWithdrawnOnTheLeadsRuling() =>
        WithdrawnRecordLoader.Read().Single(record => record.Slug == "macbannites").OwnRecord
            .Should().NotBeNullOrWhiteSpace();

    [Fact]
    public void TheGreatMenAndHisSonAreRuledOutAsNoName()
    {
        var listed = WithdrawnRecordLoader.Read().ToDictionary(record => record.Slug);

        listed["haggedolim"].OwnerRuled.Should().NotBeNullOrWhiteSpace();
        listed["beno"].OwnerRuled.Should().NotBeNullOrWhiteSpace();
    }

    /// <summary>
    /// A withdrawn address leads only to a record that stays, never to itself or to another withdrawn
    /// record, and the API reads from the list the same answer the Forge does.
    /// </summary>
    [Fact]
    public void AWithdrawnAddressLeadsOnlyToARecordThatStays()
    {
        var listed = WithdrawnRecordLoader.Read();
        var slugs = listed.Select(record => record.Slug).ToHashSet(StringComparer.Ordinal);

        listed.Where(record => record.LeadsTo is not null)
            .Should().OnlyContain(record => record.LeadsTo != record.Slug && !slugs.Contains(record.LeadsTo!));
        listed.Should().OnlyContain(record =>
            WithdrawnAddresses.IsWithdrawn(record.Slug) && WithdrawnAddresses.LeadsTo(record.Slug) == record.LeadsTo);
        WithdrawnAddresses.LeadsTo("gibbar-2").Should().Be("gibeon", "the children of Gibbar (EZR 2:20) are Nehemiah's children of Gibeon (NEH 7:25)");
        WithdrawnAddresses.LeadsTo("mahalab").Should().BeNull("JOS 19:29 names nothing there");
        WithdrawnAddresses.IsWithdrawn("gibbar").Should().BeFalse();
    }
}
