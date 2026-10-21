using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text.Json;
using Xunit;

namespace PandoraOverlay.Tests;

/// <summary>The status line's words for a failed poll: one per case the log tells apart, never a message.</summary>
public class ConnectionProblemTests
{
    private const string Secret = "MESSAGESECRET connect.sid=s%3Aabc";

    private static HttpRequestException Status(HttpStatusCode status) => new(Secret, null, status);

    [Fact]
    public void TheWebsitesBlockPageReadsAsBlocked()
    {
        // The site's own refusal (a JSON 403) is SessionEndedException; a 403 that reaches here is the block page.
        Assert.Equal("blocked by the website", ConnectionProblem.Words(Status(HttpStatusCode.Forbidden)));
    }

    [Fact]
    public void TheSitesRefusalIsASessionRefused()
    {
        Assert.Equal("session refused", ConnectionProblem.Words(new SessionEndedException()));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, "rate-limited")]
    [InlineData(HttpStatusCode.InternalServerError, "website error 500")]
    [InlineData(HttpStatusCode.BadGateway, "website error 502")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "website error 503")]
    [InlineData(HttpStatusCode.Found, "website answered 302")]
    [InlineData(HttpStatusCode.NotFound, "website answered 404")]
    [InlineData(HttpStatusCode.Unauthorized, "website answered 401")]
    public void AnyOtherStatusIsNamed(HttpStatusCode status, string expected)
    {
        Assert.Equal(expected, ConnectionProblem.Words(Status(status)));
    }

    [Fact]
    public void NoConnectionNoAnswerAndAnUnreadableAnswerAreToldApart()
    {
        Assert.Equal("no connection", ConnectionProblem.Words(new HttpRequestException(HttpRequestError.NameResolutionError, Secret)));
        Assert.Equal("no connection", ConnectionProblem.Words(new HttpRequestException(HttpRequestError.ConnectionError, Secret,
                                                                                         new SocketException((int)SocketError.ConnectionRefused))));
        Assert.Equal("secure connection failed", ConnectionProblem.Words(new HttpRequestException(HttpRequestError.SecureConnectionError, Secret)));
        Assert.Equal("no answer", ConnectionProblem.Words(new TaskCanceledException(Secret, new TimeoutException())));
        Assert.Equal("unreadable answer", ConnectionProblem.Words(new JsonException(Secret)));
    }

    [Fact]
    public void AnythingElseKeepsItsTypesName_NeverItsMessage()
    {
        var words = ConnectionProblem.Words(new InvalidOperationException(Secret));
        Assert.Equal("InvalidOperationException", words);
        Assert.DoesNotContain("SECRET", words);
    }
}
