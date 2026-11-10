using Mawrid.Domain.Suppliers;

namespace Mawrid.Application.Suppliers;

public sealed record SupplierSummaryDto(
    Guid Id, string NameAr, string NameEn, string VatNumber, string Status, bool IsAwardable);

public sealed record SupplierDto(
    Guid Id,
    string NameAr,
    string NameEn,
    string VatNumber,
    string CrNumber,
    string Status,
    string? StatusReason,
    bool IsAwardable,
    bool HasPendingBankChange,
    SupplierContactDto Contact,
    IReadOnlyList<SupplierDocumentDto> Documents,
    IReadOnlyList<BankAccountDto> BankAccounts);

public sealed record SupplierContactDto(string Name, string Email, string? Phone);

public sealed record SupplierDocumentDto(Guid Id, string Type, Guid FileId, DateOnly ExpiresOn, bool IsValid, DateTimeOffset UploadedAtUtc);

/// <summary>The IBAN is masked: only the last 4 characters are shown.</summary>
public sealed record BankAccountDto(
    Guid Id, string MaskedIban, string BankName, string AccountName, string Status, DateTimeOffset RequestedAtUtc, DateTimeOffset? VerifiedAtUtc);

internal static class SupplierMapping
{
    public static SupplierDto ToDto(Supplier supplier, DateOnly today) => new(
        supplier.Id,
        supplier.NameAr,
        supplier.NameEn,
        supplier.VatNumber.Value,
        supplier.CrNumber.Value,
        supplier.Status.ToString(),
        supplier.StatusReason,
        supplier.IsAwardable(today),
        supplier.HasPendingBankChange,
        new SupplierContactDto(supplier.Contact.Name, supplier.Contact.Email, supplier.Contact.Phone),
        supplier.CurrentDocuments
            .OrderBy(d => d.Type)
            .Select(d => new SupplierDocumentDto(d.Id, d.Type.ToString(), d.FileId, d.ExpiresOn, d.IsValidOn(today), d.UploadedAtUtc))
            .ToList(),
        supplier.BankAccounts
            .OrderByDescending(a => a.RequestedAtUtc)
            .Select(a => new BankAccountDto(a.Id, a.Iban.Masked, a.BankName, a.AccountName, a.Status.ToString(), a.RequestedAtUtc, a.VerifiedAtUtc))
            .ToList());
}
