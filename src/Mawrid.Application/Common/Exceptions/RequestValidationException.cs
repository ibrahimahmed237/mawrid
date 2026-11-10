namespace Mawrid.Application.Common.Exceptions;

/// <summary>
/// Thrown when a request has invalid input (missing fields, bad formats).
/// The API turns it into HTTP 400 with the field errors.
/// Business rule failures are not exceptions: they are returned as a failed Result.
/// </summary>
public sealed class RequestValidationException : Exception
{
    public RequestValidationException(IReadOnlyDictionary<string, string[]> errors)
        : base("One or more validation errors occurred.") => Errors = errors;

    public IReadOnlyDictionary<string, string[]> Errors { get; }
}
