using Mawrid.Api.Common.Observability;
using Microsoft.AspNetCore.Http;
using Serilog.Events;

namespace Mawrid.Api.Tests.Common;

public class RequestLogLevelTests
{
    [Theory]
    [InlineData("/api/v1/suppliers", 200, LogEventLevel.Information)]
    [InlineData("/api/v1/suppliers", 404, LogEventLevel.Information)]
    [InlineData("/api/v1/suppliers", 500, LogEventLevel.Error)]
    [InlineData("/health/ready", 200, LogEventLevel.Verbose)]
    [InlineData("/health/ready", 503, LogEventLevel.Error)]
    public void Level_depends_on_path_and_status(string path, int status, LogEventLevel expected)
    {
        var context = new DefaultHttpContext();
        context.Request.Path = path;
        context.Response.StatusCode = status;

        RequestLogLevel.For(context, 1, null).Should().Be(expected);
    }

    [Fact]
    public void An_exception_is_always_an_error()
    {
        RequestLogLevel.For(new DefaultHttpContext(), 1, new InvalidOperationException()).Should().Be(LogEventLevel.Error);
    }
}
