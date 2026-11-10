using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mawrid.Api.Tests.Infrastructure;

namespace Mawrid.Api.Tests.Contract;

/// <summary>
/// The API contract: the OpenAPI document must match the saved snapshot. A change to an endpoint or
/// DTO fails this test on purpose, so contract changes are seen in review. To accept a change, run
/// the tests with UPDATE_SNAPSHOTS=1 and commit the new snapshot.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class OpenApiContractTests(MawridApiFactory factory)
{
    private static readonly JsonSerializerOptions Indented = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    [Fact]
    public async Task The_openapi_document_matches_the_snapshot()
    {
        var json = await factory.CreateClient().GetStringAsync("/openapi/v1.json");
        var actual = JsonNode.Parse(json)!;
        actual.AsObject().Remove("servers"); // the test server's address changes
        var normalized = actual.ToJsonString(Indented).ReplaceLineEndings("\n") + "\n";

        var path = Path.Combine(RepositoryRoot(), "tests", "Mawrid.Api.Tests", "Contract", "openapi.v1.json");
        if (Environment.GetEnvironmentVariable("UPDATE_SNAPSHOTS") == "1" || !File.Exists(path))
        {
            await File.WriteAllTextAsync(path, normalized);
        }

        var expected = (await File.ReadAllTextAsync(path)).ReplaceLineEndings("\n");
        normalized.Should().Be(expected, "the API contract changed; if that is intended, run the tests with UPDATE_SNAPSHOTS=1 and commit the snapshot");
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(directory.FullName, "Mawrid.slnx")))
        {
            directory = directory.Parent ?? throw new InvalidOperationException("Mawrid.slnx not found.");
        }

        return directory.FullName;
    }
}
