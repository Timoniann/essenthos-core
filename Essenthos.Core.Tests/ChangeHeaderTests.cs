using Essenthos.Core.Accounts;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Essenthos.Core.Tests;

/// <summary>
/// Which requests must carry the header a page's script sets: the changes a browser would send with
/// the session cookie on its own, and nothing it reads or a program signs with a token.
/// </summary>
public sealed class ChangeHeaderTests
{
    private static HttpRequest Request(string method, string path, bool cookie, bool header = false, bool bearer = false)
    {
        var request = new DefaultHttpContext().Request;
        request.Method = method;
        request.Path = path;
        if (cookie)
        {
            request.Headers.Cookie = $"{SessionTokens.CookieName}={SessionTokens.New()}";
        }

        if (header)
        {
            request.Headers[ChangeHeader.Name] = "essenthos";
        }

        if (bearer)
        {
            request.Headers.Authorization = $"Bearer {SessionTokens.New()}";
        }

        return request;
    }

    [Theory]
    [InlineData("POST", "/v1/auth/logout")]
    [InlineData("POST", "/v1/me/devices/0198c7e0-0000-7000-8000-000000000000/sign-out")]
    [InlineData("PATCH", "/v1/me/bookmarks/0198c7e0-0000-7000-8000-000000000000")]
    [InlineData("PUT", "/v1/me/photo")]
    [InlineData("DELETE", "/v1/me")]
    public void AChangeTheCookieCarriesNeedsTheHeader(string method, string path)
    {
        ChangeHeader.Refuses(Request(method, path, cookie: true)).Should().BeTrue();
        ChangeHeader.Refuses(Request(method, path, cookie: true, header: true)).Should().BeFalse();
    }

    [Fact]
    public void ReadingATokenAndABrowsersReportAreNotAsked()
    {
        ChangeHeader.Refuses(Request("GET", "/v1/me", cookie: true)).Should().BeFalse();
        ChangeHeader.Refuses(Request("HEAD", "/v1/me", cookie: true)).Should().BeFalse();
        ChangeHeader.Refuses(Request("OPTIONS", "/v1/me/bookmarks", cookie: true)).Should().BeFalse();
        ChangeHeader.Refuses(Request("POST", "/v1/me/bookmarks", cookie: false, bearer: true)).Should().BeFalse();
        ChangeHeader.Refuses(Request("POST", "/v1/me/bookmarks", cookie: false)).Should().BeFalse();
        ChangeHeader.Refuses(Request("POST", "/v1/csp-reports", cookie: true)).Should().BeFalse();
    }
}
