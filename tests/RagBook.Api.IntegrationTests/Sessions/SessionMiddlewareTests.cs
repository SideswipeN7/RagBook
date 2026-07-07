using System.Globalization;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using RagBook.Api.IntegrationTests.Testing;
using RagBook.API.Sessions;
using RagBook.Shared.Sessions;
using Xunit;

namespace RagBook.Api.IntegrationTests.Sessions;

/// <summary>
/// Focused tests of the cookie/GUID logic in <see cref="SessionMiddleware"/> (AC-1, AC-2). These run
/// without a database, so they validate the session issuance rules independently of Docker.
/// </summary>
public sealed class SessionMiddlewareTests
{
    private static readonly DateTimeOffset Now = new(2026, 07, 07, 12, 00, 00, TimeSpan.Zero);

    private sealed class CapturingInitializer : ISessionInitializer
    {
        public Guid Captured { get; private set; }

        public void Initialize(Guid userSessionId)
        {
            Captured = userSessionId;
        }
    }

    private static async Task<(HttpContext Context, CapturingInitializer Initializer)> RunAsync(string? incomingCookie)
    {
        var options = Options.Create(new SessionCookieOptions());
        var middleware = new SessionMiddleware(_ => Task.CompletedTask, options, new FixedTimeProvider(Now));

        var context = new DefaultHttpContext();
        if (incomingCookie is not null)
        {
            context.Request.Headers.Cookie = $"{options.Value.CookieName}={incomingCookie}";
        }

        var initializer = new CapturingInitializer();
        await middleware.InvokeAsync(context, initializer);

        return (context, initializer);
    }

    private static string SetCookieHeader(HttpContext context)
    {
        return context.Response.Headers.SetCookie.ToString();
    }

    private static Guid CookieGuid(HttpContext context, string cookieName)
    {
        var header = SetCookieHeader(context);
        var value = header.Split(';')[0].Split('=', 2)[1];

        return Guid.Parse(value);
    }

    [Fact]
    public async Task Should_IssueVersion4CookieWithMandatedFlags_When_RequestHasNoCookie()
    {
        // Arrange & Act
        (HttpContext context, CapturingInitializer initializer) = await RunAsync(incomingCookie: null);

        // Assert
        var setCookie = SetCookieHeader(context);
        var lower = setCookie.ToLowerInvariant();
        setCookie.Should().Contain("ragbook_session=");
        lower.Should().Contain("httponly");
        lower.Should().Contain("secure");
        lower.Should().Contain("samesite=strict");
        lower.Should().Contain("expires=");

        CookieGuid(context, "ragbook_session").Version.Should().Be(4);
        initializer.Captured.Should().NotBe(Guid.Empty);
        context.Items[SessionMiddleware.IsNewSessionItemKey].Should().Be(true);
    }

    [Fact]
    public async Task Should_RefreshExpiryToThirtyDays_When_RequestHasValidCookie()
    {
        // Arrange
        var existing = Guid.NewGuid();

        // Act
        (HttpContext context, CapturingInitializer initializer) = await RunAsync(existing.ToString());

        // Assert — same identity carried, expiry slid to 30 days from "now".
        initializer.Captured.Should().Be(existing);
        context.Items[SessionMiddleware.IsNewSessionItemKey].Should().Be(false);
        var expectedDate = Now.AddDays(30).UtcDateTime.ToString("dd MMM yyyy", CultureInfo.InvariantCulture);
        SetCookieHeader(context).Should().Contain(expectedDate);
    }

    [Theory]
    [InlineData("not-a-guid")]
    [InlineData("00000000-0000-0000-0000-000000000000")]
    public async Task Should_StartFreshEmptySession_When_CookieIsForgedOrEmpty(string forged)
    {
        // Arrange & Act
        (HttpContext context, CapturingInitializer initializer) = await RunAsync(forged);

        // Assert — forged/empty cookie yields a brand-new session, never an error.
        context.Items[SessionMiddleware.IsNewSessionItemKey].Should().Be(true);
        initializer.Captured.Should().NotBe(Guid.Empty);
        CookieGuid(context, "ragbook_session").Version.Should().Be(4);
    }
}
