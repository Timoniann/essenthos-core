namespace Essenthos.Core.Tests;

/// <summary>
/// Marks the test classes that read the corpus on disk — the gigabyte under <c>Resources/</c> that
/// stays out of git — rather than only fixtures of their own. They are about a third of the tests
/// and most of the suite's time, and they are the ones a checkout without the corpus cannot run, so
/// <c>dotnet test --filter "Category!=Corpus"</c> is the run to iterate with. The full run is
/// unchanged and still the one that says a change is finished.
/// </summary>
public static class TestCategory
{
    public const string Name = "Category";
    public const string Corpus = "Corpus";
}
