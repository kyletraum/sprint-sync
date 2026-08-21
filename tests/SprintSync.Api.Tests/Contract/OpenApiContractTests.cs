using System.IO;
using System.Net;
using System.Text.Json;
using SprintSync.Api.Tests.Infrastructure;
using YamlDotNet.Serialization;

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

        // OrganizationDetail (allOf: OrganizationSummary + memberCount/createdAt) —
        // the schema the reviewer flagged as uncovered (P1-4).
        var detail = ResponseSchemaProps(root, "/api/v1/organizations/{organizationId}", "get", "200");
        Assert.Contains("id", detail);
        Assert.Contains("name", detail);
        Assert.Contains("role", detail);
        Assert.Contains("memberCount", detail);
        Assert.Contains("createdAt", detail);
    }

    [Fact]
    public async Task ContractFile_OperationsMatchTheServedDocument()
    {
        // The hand-written contract file is the agreed shape — actually PARSE it
        // (not merely reference it in a comment) and hold it equal to what the API
        // serves, so the file and the implementation cannot drift (P1-4).
        var fileOps = ContractOperationNodes(await LoadContractAsync())
            .Select(operation => operation.Operation);

        Assert.Equal(fileOps.OrderBy(x => x), (await GetServedOperationsAsync()).OrderBy(x => x));
    }

    [Fact]
    public async Task ContractFile_ResponseStatusCodes_MatchTheServedDocument()
    {
        // Route parity is not contract parity: an endpoint that quietly stops
        // documenting its 404 (or starts promising a 409 nothing returns) is drift
        // the operation-set check cannot see. Hold the declared status codes of
        // every contracted operation equal, both ways (T052, handoff item 6).
        var contract = await LoadContractAsync();
        using var document = JsonDocument.Parse(await ServedJsonAsync());
        var servedPaths = document.RootElement.GetProperty("paths");

        foreach (var (operation, node) in ContractOperationNodes(contract))
        {
            var contracted = Map(node["responses"]).Keys
                .Select(key => key.ToString()!)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray();

            var served = ServedOperation(servedPaths, operation)
                .GetProperty("responses").EnumerateObject()
                .Select(response => response.Name)
                .OrderBy(code => code, StringComparer.Ordinal)
                .ToArray();

            Assert.True(
                contracted.SequenceEqual(served, StringComparer.Ordinal),
                $"{operation}: the contract declares responses [{string.Join(", ", contracted)}] "
                + $"but the served document declares [{string.Join(", ", served)}].");
        }
    }

    [Fact]
    public async Task ContractFile_RequestBodies_MatchTheServedDocument()
    {
        // The request half of the contract, previously unchecked: which operations
        // take a body at all, the exact field set that body accepts, and that every
        // field the contract marks REQUIRED is one the server actually models —
        // so a renamed or dropped required field cannot land green (T052).
        //
        // Deliberately compares the contract's `required` list against the served
        // schema's PROPERTIES rather than against a served `required` list:
        // System.Text.Json does not mark non-optional constructor parameters as
        // required in the generated schema unless RespectRequiredConstructorParameters
        // is on, so a served-side `required` comparison would assert a serializer
        // setting rather than the contract.
        var contract = await LoadContractAsync();
        using var document = JsonDocument.Parse(await ServedJsonAsync());
        var root = document.RootElement;
        var servedPaths = root.GetProperty("paths");

        var bodiesChecked = 0;
        foreach (var (operation, node) in ContractOperationNodes(contract))
        {
            var contractBody = node.TryGetValue("requestBody", out var body) ? Map(body) : null;
            var servedOperation = ServedOperation(servedPaths, operation);
            var servedHasBody = servedOperation.TryGetProperty("requestBody", out var servedBody);

            Assert.True(
                (contractBody is not null) == servedHasBody,
                $"{operation}: the contract {(contractBody is not null ? "declares a" : "declares no")} "
                + $"request body but the served document {(servedHasBody ? "declares one" : "declares none")}.");

            if (contractBody is null)
            {
                continue;
            }

            bodiesChecked++;

            var contractSchema = ResolveContractSchema(contract, JsonSchemaOf(contractBody));
            var contractProps = new List<string>();
            CollectContractProps(contract, contractSchema, contractProps);
            Assert.NotEmpty(contractProps);

            var servedProps = new List<string>();
            CollectProps(root, servedBody.GetProperty("content")
                .GetProperty("application/json").GetProperty("schema"), servedProps);

            var expected = contractProps.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            var actual = servedProps.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            Assert.True(
                expected.SequenceEqual(actual, StringComparer.Ordinal),
                $"{operation}: the contract's request body accepts [{string.Join(", ", expected)}] "
                + $"but the served document accepts [{string.Join(", ", actual)}].");

            string[] required = contractSchema.TryGetValue("required", out var declared)
                ? ((IEnumerable<object>)declared).Select(field => field.ToString()!).ToArray()
                : [];

            // A body schema with nothing required would make the loop below vacuous.
            Assert.NotEmpty(required);

            foreach (var field in required)
            {
                Assert.True(
                    servedProps.Contains(field, StringComparer.Ordinal),
                    $"{operation}: the contract marks '{field}' as required but the served request "
                    + "schema has no such property.");
            }
        }

        // Guards the whole test against silently passing if the contract ever stops
        // declaring request bodies at all.
        Assert.True(bodiesChecked > 0, "No contracted operation declared a request body.");
    }

    /// <summary>The parsed contract file, YamlDotNet's untyped node graph.</summary>
    private static async Task<Dictionary<string, object>> LoadContractAsync() =>
        new DeserializerBuilder().Build()
            .Deserialize<Dictionary<string, object>>(await File.ReadAllTextAsync(ContractYamlPath()));

    /// <summary>YamlDotNet yields nested mappings as object-keyed dictionaries.</summary>
    private static IDictionary<object, object> Map(object node) => (IDictionary<object, object>)node;

    /// <summary>
    /// Every operation the contract file declares, as ("METHOD /api/v1/path", node).
    /// The contract's server is /api/v1; the served document uses full paths.
    /// </summary>
    private static IEnumerable<(string Operation, IDictionary<object, object> Node)> ContractOperationNodes(
        IDictionary<string, object> contract)
    {
        var methods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "get", "post", "put", "delete", "patch" };

        foreach (var path in Map(contract["paths"]))
        {
            foreach (var verb in Map(path.Value))
            {
                var method = verb.Key.ToString()!;
                if (methods.Contains(method))
                {
                    yield return ($"{method.ToUpperInvariant()} /api/v1{path.Key}", Map(verb.Value));
                }
            }
        }
    }

    /// <summary>The served document's node for a "METHOD path" operation.</summary>
    private static JsonElement ServedOperation(JsonElement servedPaths, string operation)
    {
        var parts = operation.Split(' ');

        Assert.True(
            servedPaths.TryGetProperty(parts[1], out var path),
            $"The served document has no path '{parts[1]}', contracted by {operation}.");
        Assert.True(
            path.TryGetProperty(parts[0].ToLowerInvariant(), out var served),
            $"The served document does not serve {operation}.");

        return served;
    }

    /// <summary>The application/json schema node of a request body or response.</summary>
    private static IDictionary<object, object> JsonSchemaOf(IDictionary<object, object> node) =>
        Map(Map(Map(node["content"])["application/json"])["schema"]);

    /// <summary>Follows a local $ref (e.g. #/components/schemas/Foo) to the schema it names.</summary>
    private static IDictionary<object, object> ResolveContractSchema(
        IDictionary<string, object> contract, IDictionary<object, object> schema)
    {
        if (!schema.TryGetValue("$ref", out var reference))
        {
            return schema;
        }

        object current = contract;
        foreach (var segment in reference.ToString()!.Split('/').Skip(1))
        {
            current = current is IDictionary<string, object> top ? top[segment] : Map(current)[segment];
        }

        return ResolveContractSchema(contract, Map(current));
    }

    /// <summary>Contract-side twin of CollectProps: property names, resolving $ref and allOf.</summary>
    private static void CollectContractProps(
        IDictionary<string, object> contract, IDictionary<object, object> schema, List<string> into)
    {
        var resolved = ResolveContractSchema(contract, schema);

        if (resolved.TryGetValue("properties", out var properties))
        {
            foreach (var property in Map(properties))
            {
                into.Add(property.Key.ToString()!);
            }
        }

        if (resolved.TryGetValue("allOf", out var allOf))
        {
            foreach (var sub in (IEnumerable<object>)allOf)
            {
                CollectContractProps(contract, Map(sub), into);
            }
        }
    }

    private static string ContractYamlPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "SprintSync.slnx")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return Path.Combine(
            dir!.FullName, "specs", "001-org-tenant-context", "contracts", "openapi.yaml");
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
