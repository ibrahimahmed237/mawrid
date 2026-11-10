using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Mawrid.Api.Tests.Infrastructure;

public static class HttpAssertions
{
    /// <summary>Asserts the status and the ProblemDetails <c>code</c> of an error response.</summary>
    public static async Task ShouldBeProblemAsync(this HttpResponseMessage response, HttpStatusCode status, string code)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        JsonDocument.Parse(body).RootElement.GetProperty("code").GetString().Should().Be(code);
    }

    public static async Task<T> ShouldBeOkAsync<T>(this HttpResponseMessage response, HttpStatusCode status = HttpStatusCode.OK)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(status, body);
        return JsonSerializer.Deserialize<T>(body, JsonSerializerOptions.Web)!;
    }
}
