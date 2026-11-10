using Mawrid.Domain.Common;

namespace Mawrid.Domain.Suppliers;

public static class SupplierErrors
{
    public static readonly Error NameInvalid =
        Error.Validation("supplier.name_invalid", "Arabic and English names must be 2 to 200 characters.");

    public static readonly Error VatNumberInvalid =
        Error.Validation("supplier.vat_number_invalid", "The VAT number must be 15 digits and start and end with 3.");

    public static readonly Error CrNumberInvalid =
        Error.Validation("supplier.cr_number_invalid", "The CR number must be 10 digits.");

    public static readonly Error VatNumberTaken =
        Error.Conflict("supplier.vat_number_taken", "A supplier with this VAT number already exists.");

    public static readonly Error IbanInvalid =
        Error.Validation("supplier.iban_invalid", "The IBAN must be a valid Saudi IBAN (SA followed by 22 characters).");

    public static readonly Error BankDetailsInvalid =
        Error.Validation("supplier.bank_details_invalid", "Bank name and account name must be 2 to 200 characters.");

    public static readonly Error BankChangeAlreadyPending =
        Error.Conflict("supplier.bank_change_pending", "A bank account change is already waiting for verification.");

    public static readonly Error BankAccountNotFound =
        Error.NotFound("supplier.bank_account_not_found", "The bank account was not found.");

    public static readonly Error BankAccountNotPending =
        Error.RuleViolation("supplier.bank_account_not_pending", "This bank account is not waiting for verification.");

    public static readonly Error BankAccountMissing =
        Error.RuleViolation("supplier.bank_account_missing", "Add a bank account before submitting.");

    public static readonly Error FourEyesRequired =
        Error.RuleViolation("supplier.four_eyes_required", "A bank account must be verified by someone other than the person who added it.");

    public static readonly Error DocumentAlreadyExpired =
        Error.Validation("supplier.document_expired", "The document's expiry date must be in the future.");

    public static readonly Error RequiredDocumentsMissing =
        Error.RuleViolation("supplier.documents_missing", "A valid CR certificate, VAT certificate and bank letter are required.");

    public static readonly Error SupplierRejected =
        Error.RuleViolation("supplier.rejected", "This supplier was rejected and cannot be changed.");

    public static readonly Error ReasonRequired =
        Error.Validation("supplier.reason_required", "A reason of 3 to 500 characters is required.");

    public static readonly Error NotFound =
        Error.NotFound("supplier.not_found", "The supplier was not found.");

    public static readonly Error FileNotFound =
        Error.NotFound("supplier.file_not_found", "The file was not found, or it is not ready yet.");

    public static Error InvalidStatusChange(SupplierStatus from, SupplierStatus to) =>
        Error.RuleViolation("supplier.invalid_status_change", $"A supplier cannot move from {from} to {to}.");
}
