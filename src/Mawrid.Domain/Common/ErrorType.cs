namespace Mawrid.Domain.Common;

/// <summary>The kind of an expected failure. The API maps each kind to an HTTP status.</summary>
public enum ErrorType
{
    /// <summary>The input is invalid. HTTP 400.</summary>
    Validation,

    /// <summary>The caller is not logged in, or the credentials are wrong. HTTP 401.</summary>
    Unauthorized,

    /// <summary>The caller is not allowed to do this. HTTP 403.</summary>
    Forbidden,

    /// <summary>The thing does not exist (or the caller cannot see it). HTTP 404.</summary>
    NotFound,

    /// <summary>The request conflicts with the current state, such as a duplicate. HTTP 409.</summary>
    Conflict,

    /// <summary>A business rule says no, such as "the RFQ deadline has passed". HTTP 422.</summary>
    RuleViolation,

    /// <summary>The client's copy is out of date (If-Match did not match). HTTP 412.</summary>
    Precondition,

    /// <summary>A service we depend on (for example the AI provider) is not answering. HTTP 503.</summary>
    Unavailable,
}
