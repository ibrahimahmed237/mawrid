namespace Mawrid.Domain.Suppliers;

public enum SupplierStatus
{
    /// <summary>Created by the buyer. The supplier is filling in documents and bank details.</summary>
    Registered = 1,

    /// <summary>The supplier submitted everything. An admin must approve or reject.</summary>
    UnderReview = 2,

    Approved = 3,
    Rejected = 4,

    /// <summary>Temporarily not allowed to win new awards. Open POs continue.</summary>
    Blocked = 5,
}

public enum SupplierDocumentType
{
    CrCertificate = 1,
    VatCertificate = 2,
    BankLetter = 3,
    Other = 4,
}

public enum BankAccountStatus
{
    PendingVerification = 1,
    Verified = 2,

    /// <summary>A newer verified account replaced this one.</summary>
    Superseded = 3,
}
