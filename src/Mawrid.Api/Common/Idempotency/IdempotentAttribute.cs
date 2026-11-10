namespace Mawrid.Api.Common.Idempotency;

/// <summary>
/// Marks a POST endpoint that honors the <c>Idempotency-Key</c> header. A retry with the same
/// key and body gets the first response again; the work is done once. See DESIGN section 9.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute;
