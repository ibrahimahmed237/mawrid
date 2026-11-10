using Mawrid.Domain.Common;

namespace Mawrid.Domain.Suppliers;

/// <summary>
/// A company the buyer can buy from. The life cycle is:
/// Registered → UnderReview → Approved ⇄ Blocked, or Rejected.
/// See DESIGN section 6.2 for the rules.
/// </summary>
public sealed class Supplier : AggregateRoot<Guid>, ITenantOwned
{
    public const int NameMaxLength = 200;
    public const int ReasonMaxLength = 500;

    /// <summary>Days before expiry that trigger a warning. -1 means "expired yesterday".</summary>
    public static readonly IReadOnlyList<int> ExpiryWarningDays = [30, 7, -1];

    /// <summary>Documents a supplier must have, and keep valid, to be approved and to win awards.</summary>
    public static readonly IReadOnlyList<SupplierDocumentType> RequiredDocuments =
        [SupplierDocumentType.CrCertificate, SupplierDocumentType.VatCertificate, SupplierDocumentType.BankLetter];

    private readonly List<SupplierDocument> _documents = [];
    private readonly List<SupplierBankAccount> _bankAccounts = [];

    private Supplier(Guid id, Guid organizationId, string nameAr, string nameEn, VatNumber vatNumber, CrNumber crNumber, SupplierContact contact)
        : base(id)
    {
        OrganizationId = organizationId;
        NameAr = nameAr;
        NameEn = nameEn;
        VatNumber = vatNumber;
        CrNumber = crNumber;
        Contact = contact;
        Status = SupplierStatus.Registered;
    }

    private Supplier()
    {
        NameAr = null!;
        NameEn = null!;
        VatNumber = null!;
        CrNumber = null!;
        Contact = null!;
    }

    public Guid OrganizationId { get; private set; }

    public string NameAr { get; private set; }

    public string NameEn { get; private set; }

    public VatNumber VatNumber { get; private set; }

    public CrNumber CrNumber { get; private set; }

    public SupplierContact Contact { get; private set; }

    public SupplierStatus Status { get; private set; }

    /// <summary>Why the supplier was rejected or blocked.</summary>
    public string? StatusReason { get; private set; }

    public IReadOnlyList<SupplierDocument> Documents => _documents;

    public IReadOnlyList<SupplierBankAccount> BankAccounts => _bankAccounts;

    /// <summary>The account payments go to: the latest verified one.</summary>
    public SupplierBankAccount? ActiveBankAccount =>
        _bankAccounts.Where(a => a.Status == BankAccountStatus.Verified).MaxBy(a => a.VerifiedAtUtc);

    /// <summary>True while a bank change waits for a second person to verify it. Payments are held until then.</summary>
    public bool HasPendingBankChange => _bankAccounts.Any(a => a.Status == BankAccountStatus.PendingVerification);

    public static Result<Supplier> Register(
        Guid organizationId, string nameAr, string nameEn, string vatNumber, string crNumber, SupplierContact contact)
    {
        nameAr = nameAr?.Trim() ?? string.Empty;
        nameEn = nameEn?.Trim() ?? string.Empty;
        if (nameAr.Length is < 2 or > NameMaxLength || nameEn.Length is < 2 or > NameMaxLength)
        {
            return SupplierErrors.NameInvalid;
        }

        var vat = VatNumber.Create(vatNumber);
        if (vat.IsFailure)
        {
            return vat.Error!;
        }

        var cr = CrNumber.Create(crNumber);
        if (cr.IsFailure)
        {
            return cr.Error!;
        }

        var supplier = new Supplier(Guid.CreateVersion7(), organizationId, nameAr, nameEn, vat.Value, cr.Value, contact);
        supplier.Raise(new SupplierRegistered(supplier.Id, organizationId));
        return supplier;
    }

    /// <summary>The latest document of each type.</summary>
    public IEnumerable<SupplierDocument> CurrentDocuments =>
        _documents.GroupBy(d => d.Type).Select(g => g.MaxBy(d => d.UploadedAtUtc)!);

    /// <summary>True when every required document exists and has not expired on <paramref name="today"/>.</summary>
    public bool HasValidRequiredDocuments(DateOnly today) =>
        RequiredDocuments.All(type => CurrentDocuments.Any(d => d.Type == type && d.IsValidOn(today)));

    /// <summary>Can the supplier be invited to RFQs and win awards today?</summary>
    public bool IsAwardable(DateOnly today) => Status == SupplierStatus.Approved && HasValidRequiredDocuments(today);

    /// <summary>
    /// Raises <see cref="SupplierDocumentExpiring"/> for each current document that expires in exactly
    /// 30 or 7 days, or that expired yesterday. A daily job calls it, so each warning goes out once.
    /// </summary>
    /// <returns>How many warnings were raised.</returns>
    public int WarnAboutExpiringDocuments(DateOnly today)
    {
        if (Status != SupplierStatus.Approved)
        {
            return 0;
        }

        var warnings = 0;
        foreach (var document in CurrentDocuments)
        {
            var daysLeft = document.ExpiresOn.DayNumber - today.DayNumber;
            if (ExpiryWarningDays.Contains(daysLeft))
            {
                Raise(new SupplierDocumentExpiring(Id, OrganizationId, document.Type, document.ExpiresOn, daysLeft));
                warnings++;
            }
        }

        return warnings;
    }

    /// <param name="today">The business date (Asia/Riyadh). A document must expire after it.</param>
    public Result AddDocument(SupplierDocumentType type, Guid fileId, DateOnly expiresOn, DateOnly today, DateTimeOffset now)
    {
        if (Status == SupplierStatus.Rejected)
        {
            return SupplierErrors.SupplierRejected;
        }

        if (expiresOn <= today)
        {
            return SupplierErrors.DocumentAlreadyExpired;
        }

        _documents.Add(new SupplierDocument(Guid.CreateVersion7(), type, fileId, expiresOn, now));
        return Result.Success();
    }

    /// <summary>
    /// Adds a bank account. Before approval it is the supplier's first account and is verified
    /// when the supplier is approved. After approval it is a bank change and needs a second person.
    /// </summary>
    public Result RequestBankAccount(string iban, string bankName, string accountName, Guid? requestedBy, DateTimeOffset now)
    {
        if (Status == SupplierStatus.Rejected)
        {
            return SupplierErrors.SupplierRejected;
        }

        var parsed = Iban.Create(iban);
        if (parsed.IsFailure)
        {
            return parsed.Error!;
        }

        bankName = bankName?.Trim() ?? string.Empty;
        accountName = accountName?.Trim() ?? string.Empty;
        if (bankName.Length is < 2 or > NameMaxLength || accountName.Length is < 2 or > NameMaxLength)
        {
            return SupplierErrors.BankDetailsInvalid;
        }

        if (HasPendingBankChange)
        {
            return SupplierErrors.BankChangeAlreadyPending;
        }

        var account = new SupplierBankAccount(Guid.CreateVersion7(), parsed.Value, bankName, accountName, requestedBy, now);
        _bankAccounts.Add(account);
        Raise(new SupplierBankAccountRequested(Id, OrganizationId, account.Id));
        return Result.Success();
    }

    /// <summary>Verifies a pending bank account. The verifier must not be the person who requested it.</summary>
    public Result VerifyBankAccount(Guid accountId, Guid verifiedBy, DateTimeOffset now)
    {
        var account = _bankAccounts.FirstOrDefault(a => a.Id == accountId);
        if (account is null)
        {
            return SupplierErrors.BankAccountNotFound;
        }

        if (account.Status != BankAccountStatus.PendingVerification)
        {
            return SupplierErrors.BankAccountNotPending;
        }

        if (account.RequestedBy == verifiedBy)
        {
            return SupplierErrors.FourEyesRequired;
        }

        foreach (var verified in _bankAccounts.Where(a => a.Status == BankAccountStatus.Verified))
        {
            verified.Supersede();
        }

        account.Verify(verifiedBy, now);
        Raise(new SupplierBankAccountVerified(Id, OrganizationId, account.Id));
        return Result.Success();
    }

    public Result SubmitForReview(DateOnly today)
    {
        if (Status != SupplierStatus.Registered)
        {
            return SupplierErrors.InvalidStatusChange(Status, SupplierStatus.UnderReview);
        }

        if (!HasValidRequiredDocuments(today))
        {
            return SupplierErrors.RequiredDocumentsMissing;
        }

        if (_bankAccounts.Count == 0)
        {
            return SupplierErrors.BankAccountMissing;
        }

        Status = SupplierStatus.UnderReview;
        Raise(new SupplierSubmittedForReview(Id, OrganizationId));
        return Result.Success();
    }

    /// <summary>Approves the supplier and verifies its first bank account (the approver is the second person).</summary>
    public Result Approve(Guid approvedBy, DateOnly today, DateTimeOffset now)
    {
        if (Status != SupplierStatus.UnderReview)
        {
            return SupplierErrors.InvalidStatusChange(Status, SupplierStatus.Approved);
        }

        if (!HasValidRequiredDocuments(today))
        {
            return SupplierErrors.RequiredDocumentsMissing;
        }

        var pending = _bankAccounts.SingleOrDefault(a => a.Status == BankAccountStatus.PendingVerification);
        if (pending is not null)
        {
            var verified = VerifyBankAccount(pending.Id, approvedBy, now);
            if (verified.IsFailure)
            {
                return verified;
            }
        }

        Status = SupplierStatus.Approved;
        StatusReason = null;
        Raise(new SupplierApproved(Id, OrganizationId));
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status != SupplierStatus.UnderReview)
        {
            return SupplierErrors.InvalidStatusChange(Status, SupplierStatus.Rejected);
        }

        return ChangeStatusWithReason(SupplierStatus.Rejected, reason, new SupplierRejected(Id, OrganizationId));
    }

    public Result Block(string reason)
    {
        if (Status != SupplierStatus.Approved)
        {
            return SupplierErrors.InvalidStatusChange(Status, SupplierStatus.Blocked);
        }

        return ChangeStatusWithReason(SupplierStatus.Blocked, reason, new SupplierBlocked(Id, OrganizationId));
    }

    public Result Unblock()
    {
        if (Status != SupplierStatus.Blocked)
        {
            return SupplierErrors.InvalidStatusChange(Status, SupplierStatus.Approved);
        }

        Status = SupplierStatus.Approved;
        StatusReason = null;
        Raise(new SupplierUnblocked(Id, OrganizationId));
        return Result.Success();
    }

    private Result ChangeStatusWithReason(SupplierStatus status, string reason, IDomainEvent domainEvent)
    {
        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 3 or > ReasonMaxLength)
        {
            return SupplierErrors.ReasonRequired;
        }

        Status = status;
        StatusReason = reason;
        Raise(domainEvent);
        return Result.Success();
    }
}

/// <summary>The main contact person at the supplier.</summary>
public sealed record SupplierContact(string Name, string Email, string? Phone);
