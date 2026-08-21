using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Contract;

/// <summary>
/// P1-3 / Principle III: an unhandled exception surfaces as RFC 9457
/// ProblemDetails — parseable, and leaking no exception type or stack frame to
/// the client (P2-6). Exercised via a test-only throwing endpoint.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class ErrorContractTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    [Fact]
    public async Task UnhandledException_IsProblemDetails500_WithoutLeakingDetail()
    {
        var response = await _factory.CreateClient().GetAsync("/api/v1/_test/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        // Parseable as ProblemDetails with a real title and the right status.
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>(TestJson.Options);
        Assert.NotNull(problem);
        Assert.Equal(500, problem!.Status);
        Assert.False(string.IsNullOrWhiteSpace(problem.Title));

        // ...and it discloses nothing about the failure: no exception type name,
        // no thrown message, no stack frames (P2-6).
        var raw = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("InvalidOperationException", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("boom", raw, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("   at ", raw);
    }
}
