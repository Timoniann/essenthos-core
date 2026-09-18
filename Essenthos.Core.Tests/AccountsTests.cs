using Essenthos.Core.Accounts;
using Essenthos.Core.Endpoints;
using FluentAssertions;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// The three places in sign-in where a small mistake is a security hole rather than a bug: where a
/// reader is sent back to, what an uploaded picture is taken to be, and what counts as a token.
/// </summary>
public sealed class AccountsTests
{
    [Theory]
    [InlineData("/read/JHN/1", "/read/JHN/1")]
    [InlineData("/account?tab=devices", "/account?tab=devices")]
    [InlineData("/", "/")]
    // Another origin, spelt four ways a browser will follow: each is how a sign-in link on this site
    // becomes a redirect to somebody else's page.
    [InlineData("//evil.example/steal", "/")]
    [InlineData("/\\evil.example", "/")]
    [InlineData("https://evil.example", "/")]
    [InlineData("evil.example", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    public void ReturnAddressesStayOnTheSite(string? requested, string expected) =>
        AuthEndpoints.Local(requested).Should().Be(expected);

    [Fact]
    public void APictureIsWhatItsBytesSayNotWhatItsRequestClaims()
    {
        MeEndpoints.Sniff([0xFF, 0xD8, 0xFF, 0xE0, 0, 0]).Should().Be("image/jpeg");
        MeEndpoints.Sniff([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0]).Should().Be("image/png");
        MeEndpoints.Sniff("RIFF\0\0\0\0WEBPVP8 "u8).Should().Be("image/webp");

        // An SVG is a document that can carry script, and it would be served from this origin.
        MeEndpoints.Sniff("<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8)
            .Should().BeNull();
        MeEndpoints.Sniff("<html><script>alert(1)</script></html>"u8).Should().BeNull();
        MeEndpoints.Sniff("RIFF\0\0\0\0WAVEfmt "u8).Should().BeNull();
        MeEndpoints.Sniff([]).Should().BeNull();
    }

    [Fact]
    public void ATokenIsLongRandomAndKeptOnlyAsItsHash()
    {
        var one = SessionTokens.New();
        var two = SessionTokens.New();

        one.Should().NotBe(two);
        one.Should().HaveLength(43, "32 random bytes, base64url without padding");
        one.Should().MatchRegex("^[A-Za-z0-9_-]+$", "it travels in a cookie and a header unescaped");

        SessionTokens.Hash(one).Should().HaveCount(32).And.Equal(SessionTokens.Hash(one));
        SessionTokens.Hash(one).Should().NotEqual(SessionTokens.Hash(two));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("a-cookie-somebody-typed-by-hand")]
    public void SomethingThatIsNotATokenIsNotLookedUp(string value) =>
        SessionTokens.Hash(value).Should().BeNull();
}
