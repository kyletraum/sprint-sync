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
    public async Task EveryContractedOperation_IsRoutable_UnderItsOwnVerb()
    {
        var client = _factory.CreateClient();

        foreach (var operation in ContractOperations)
        {
            var parts = operation.Split(' ');
            var path = parts[1].Replace("{organizationId}", Guid.NewGuid().ToString());
            using var request = new HttpRequestMessage(new HttpMethod(parts[0]), path);
            var response = await client.SendAsync(request);

            // Sent under the CONTRACTED verb, unauthenticated: 401 is expected. A
            // 404 or 405 would mean the contract promises an operation nothing
            // serves under that method (P2-14).
            Assert.False(
                response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed,
                $"{operation} is contracted but not served under its verb (got {(int)response.StatusCode}).");
        }
    }

    [Fact]
    public async Task ContractedResponseBodies_AreSchematisedInTheServedDocument()
    {
        using var document = JsonDocument.Parse(await ServedJsonAsync());
        var root = document.RootElement;

        // Body shape, not just route existence: a field rename or a changed
        // pagination/response envelope (Principles II/III) must fail here (P1-7).
        var me = ResponseSchemaProps(root, "/api/v1/me", "get", "200");
        Assert.Contains("userId", me);
        Assert.Contains("activeOrganizationId", me);

        var created = ResponseSchemaProps(root, "/api/v1/organizations", "post", "201");
        Assert.Contains("id", created);
        Assert.Contains("name", created);
        Assert.Contains("role", created);

        var page = ResponseSchemaProps(root, "/api/v1/organizations", "get", "200");
        Assert.Contains("items", page);
        Assert.Contains("totalCount", page);
    }

    private async Task<List<string>> GetServedOperationsAsync()
    {
        using var document = JsonDocument.Parse(await ServedJsonAsync());

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

    private async Task<string> ServedJsonAsync()
    {
        var response = await _factory.CreateClient().GetAsync("/openapi/v1.json");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    /// <summary>Property names of an operation's JSON response body schema, resolving $ref and allOf.</summary>
    private static IReadOnlyCollection<string> ResponseSchemaProps(
        JsonElement root, string path, string method, string status)
    {
        var schema = root.GetProperty("paths").GetProperty(path).GetProperty(method)
            .GetProperty("responses").GetProperty(status)
            .GetProperty("content").GetProperty("application/json").GetProperty("schema");

        var props = new List<string>();
        CollectProps(root, schema, props);
        Assert.NotEmpty(props);
        return props;
    }

    private static void CollectProps(JsonElement root, JsonElement schema, List<string> into)
    {
        if (schema.TryGetProperty("$ref", out var refEl))
        {
            var name = refEl.GetString()!.Split('/')[^1];
            CollectProps(root, root.GetProperty("components").GetProperty("schemas").GetProperty(name), into);
            return;
        }

        if (schema.TryGetProperty("properties", out var properties))
        {
            foreach (var property in properties.EnumerateObject())
            {
                into.Add(property.Name);
            }
        }

        if (schema.TryGetProperty("allOf", out var allOf))
        {
            foreach (var sub in allOf.EnumerateArray())
            {
                CollectProps(root, sub, into);
            }
        }
    }
}
