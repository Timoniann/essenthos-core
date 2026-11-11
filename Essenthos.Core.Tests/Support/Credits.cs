namespace Essenthos.Core.Tests;

/// <summary>The credits and kinds the records carry, as a fixture writes them onto the rows it seeds.</summary>
internal static class Credits
{
    public const string BibleData = "BibleData by Brady Stephenson, github.com/BradyStephenson/bible-data, CC BY 4.0";

    public const string OpenBible = "OpenBible.info Bible Geocoding, github.com/openbibleinfo/Bible-Geocoding-Data, CC BY 4.0";

    /// <summary>Where a people's verse list comes from, which is this corpus and not a dataset.</summary>
    public const string OurPeopleWords = "Essenthos, from the words this corpus annotates to the people";

    /// <summary>A name row that is a record's proper name, beside a title.</summary>
    public const string ProperName = "proper name";
}
