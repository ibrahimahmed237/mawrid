namespace Mawrid.Domain.Common;

/// <summary>
/// An expected failure with a stable, machine-readable code such as
/// <c>rfq.deadline_passed</c>. Clients branch on the code, not on the message.
/// </summary>
public sealed record Error
{
    private Error(string code, string message, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
        Type = type;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorType Type { get; }

    public static Error Validation(string code, string message) => new(code, message, ErrorType.Validation);

    public static Error Unauthorized(string code, string message) => new(code, message, ErrorType.Unauthorized);

    public static Error Forbidden(string code, string message) => new(code, message, ErrorType.Forbidden);

    public static Error NotFound(string code, string message) => new(code, message, ErrorType.NotFound);

    public static Error Conflict(string code, string message) => new(code, message, ErrorType.Conflict);

    public static Error RuleViolation(string code, string message) => new(code, message, ErrorType.RuleViolation);

    public static Error Precondition(string code, string message) => new(code, message, ErrorType.Precondition);
}
