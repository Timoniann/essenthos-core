using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// What the reader's banner follows. A corpus that fails an integrity check is a fault for whoever
/// builds it; every text in it still reads, so the status must not tell a reader that parts of the
/// site are unavailable.
/// </summary>
public sealed class HealthStatusTests
{
    private static VerificationResponse Verified(int broken) =>
        new(DateTimeOffset.UtcNow, broken, 0.9, 900, 1000);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(17)]
    public void ALoadedCorpusIsReadyWhateverItsIntegrityChecksSay(int broken)
    {
        HealthEndpoints.Status(66, Verified(broken)).Should().Be("ready");
        HealthEndpoints.Missing(66, Verified(broken)).Should().BeEmpty();
    }

    [Fact]
    public void AnEmptyCorpusIsStillLoadingAndAnUnmeasuredOneSaysSo()
    {
        HealthEndpoints.Status(0, null).Should().Be("loading");
        HealthEndpoints.Status(0, Verified(3)).Should().Be("loading");
        HealthEndpoints.Missing(66, null).Should().ContainSingle().Which.Should().Contain("not been measured");
    }
}
