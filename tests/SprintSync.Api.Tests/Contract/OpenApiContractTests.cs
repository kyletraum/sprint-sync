using System.Net;
using System.Text.Json;
using SprintSync.Api.Tests.Infrastructure;

namespace SprintSync.Api.Tests.Contract;

/// <summary>
/// T046 / Principle III: the OpenAPI document is a published deliverable, and
/// <c>contracts/openapi.yaml</c> is the agreed shape of it.
///
/// This compares the served document against the hand-written contract so the
/// two cannot drift silently — adding an endpoint without contracting it, or
/// contracting one that was never built, both fail here.
/// </summary>
[Collection(SqlServerCollection.Name)]
public sealed class OpenApiContractTests(SqlServerFixture sql) : IDisposable
{
    private readonly SprintSyncApiFactory _factory = new(sql.ConnectionString);

    public void Dispose() => _factory.Dispose();

    /// <summary>Operations the contract promises, as "METHOD path" (server-relative).</summary>
    private static readonly string[] ContractOperations =
    [
        "GET /api/v1/me",
        "PUT /api/v1/me/active-organization",
        "GET /api/v1/organizations",
        "POST /api/v1/organizations",
        "GET /api/v1/organizations/{organizationId}",
    ];

    [Fact]
    public async Task OpenApiDocument_IsPublished()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");

        // Published, and published anonymously: a contract nobody can fetch
        // without credentials is not a public contract (Principle III).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Sprint Sync API", document.RootElement
            .GetProperty("info").GetProperty("title").GetString());
    }

    [Fact]
    public async Task ServedDocument_ExposesExactlyTheContractedOperations()
    {
        var served = await GetServedOperationsAsync();

        // Set equality both ways: no undocumented endpoint, no undelivered promise.
        Assert.Equal(ContractOperations.OrderBy(x => x), served.OrderBy(x => x));
    }

    [Fact]
    public async Task EveryContractedOperation_IsRoutable()
    {
        var client = _factory.CreateClient();

        foreach (var operation in ContractOperations)
        {
            var path = operation.Split(' ')[1].Replace("{organizationId}", Guid.NewGuid().ToString());
            var response = await client.GetAsync(path);

            // Unauthenticated, so 401 is the right answer — what matters is that
            // the route exists at all. A 404 would mean the contract promises a
            // path nothing serves.
            Assert.False(
                response.StatusCode == HttpStatusCode.NotFound,
                $"{operation} is in the contract but no route serves '{path}'.");
        }
    }

    private async Task<List<string>> GetServedOperationsAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        var operations = new List<string>();
        foreach (var path in document.RootElement.GetProperty("paths").EnumerateObject())
        {
            foreach (var method in path.Value.EnumerateObject())
            {
                operations.Add($"{method.Name.ToUpperInvariant()} {path.Name}");
            }
        }

        return operations;
    }
}
