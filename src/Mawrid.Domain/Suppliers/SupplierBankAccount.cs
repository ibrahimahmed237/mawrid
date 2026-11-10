namespace Mawrid.Domain.Suppliers;

/// <summary>
/// A bank account the supplier wants to be paid into. A new account must be verified
/// by a second person before payments can go to it (four-eyes rule).
/// </summary>
public sealed class SupplierBankAccount
{
    internal SupplierBankAccount(Guid id, Iban iban, string bankName, string accountName, Guid? requestedBy, DateTimeOffset requestedAtUtc)
    {
        Id = id;
        Iban = iban;
        BankName = bankName;
        AccountName = accountName;
        RequestedBy = requestedBy;
        RequestedAtUtc = requestedAtUtc;
        Status = BankAccountStatus.PendingVerification;
    }

    private SupplierBankAccount()
    {
        Iban = null!;
        BankName = null!;
        AccountName = null!;
    }

    public Guid Id { get; private set; }

    public Iban Iban { get; private set; }

    public string BankName { get; private set; }

    public string AccountName { get; private set; }

    public BankAccountStatus Status { get; private set; }

    public Guid? RequestedBy { get; private set; }

    public DateTimeOffset RequestedAtUtc { get; private set; }

    public Guid? VerifiedBy { get; private set; }

    public DateTimeOffset? VerifiedAtUtc { get; private set; }

    internal void Verify(Guid verifiedBy, DateTimeOffset now)
    {
        Status = BankAccountStatus.Verified;
        VerifiedBy = verifiedBy;
        VerifiedAtUtc = now;
    }

    internal void Supersede() => Status = BankAccountStatus.Superseded;
}
