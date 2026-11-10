namespace Mawrid.Api.Common.Errors;

/// <summary>Error codes that the API itself produces (not a business module).</summary>
public static class ErrorCodes
{
    public const string ValidationFailed = "request.validation_failed";
    public const string Unexpected = "server.unexpected_error";
    public const string NotFound = "request.not_found";
    public const string Unauthenticated = "auth.unauthenticated";
    public const string Forbidden = "auth.forbidden";
    public const string MethodNotAllowed = "request.method_not_allowed";
    public const string Http = "request.http_error";
    public const string ConcurrencyConflict = "concurrency.conflict";
    public const string RateLimited = "request.rate_limited";
}
