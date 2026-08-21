using System.Net;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Contract;

/// <summary>
/// P1-3 / Principle III: an unhandled exception surfaces as RFC 9457
/// ProblemDetails, not a bare bodyless 500 the web app and third-party clients
/// cannot parse. Exercised via a test-only throwing endpoint.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ErrorContractTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task UnhandledException_IsRenderedAsProblemDetails500()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/_test/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
