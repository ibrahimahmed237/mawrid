using Mawrid.Domain.Approvals;

namespace Mawrid.Application.Approvals;

/// <param name="Version">The row version (base64). Send it back as an <c>If-Match</c> header when you decide.</param>
public sealed record ApprovalDto(
    Guid Id,
    string DocumentType,
    Guid DocumentId,
    string DocumentTitle,
    decimal Amount,
    Guid CostCenterId,
    Guid RequestedBy,
    DateTimeOffset RequestedAtUtc,
    string Status,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<ApprovalStepDto> Steps,
    string Version);

public sealed record ApprovalStepDto(
    int Order, string Name, string Status, int EligibleApproverCount, Guid? DecidedBy, Guid? OnBehalfOf, DateTimeOffset? DecidedAtUtc, string? Comment);

public sealed record InboxItemDto(Guid Id, string DocumentType, Guid DocumentId, string DocumentTitle, decimal Amount, string StepName, DateTimeOffset RequestedAtUtc, bool AsDelegate);

internal static class ApprovalMapping
{
    public static ApprovalDto ToDto(ApprovalRequest request) => new(
        request.Id,
        request.DocumentType,
        request.DocumentId,
        request.DocumentTitle,
        request.Amount,
        request.CostCenterId,
        request.RequestedBy,
        request.RequestedAtUtc,
        request.Status.ToString(),
        request.CompletedAtUtc,
        request.Steps.OrderBy(s => s.Order)
            .Select(s => new ApprovalStepDto(s.Order, s.Name, s.Status.ToString(), s.EligibleApproverIds.Count, s.DecidedBy, s.OnBehalfOf, s.DecidedAtUtc, s.Comment))
            .ToList(),
        Convert.ToBase64String(request.RowVersion));
}
