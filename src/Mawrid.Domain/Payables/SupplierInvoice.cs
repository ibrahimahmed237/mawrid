using Mawrid.Domain.Common;

namespace Mawrid.Domain.Payables;

/// <summary>
/// A supplier's tax invoice for one purchase order. Life cycle:
/// Received → Matched or OnHold → ReleasedForPayment, or Rejected. See DESIGN section 6.7.
/// Amounts are stored as the supplier stated them; <see cref="InvoiceChecker"/> compares them with ours.
/// </summary>
public sealed class SupplierInvoice : AggregateRoot<Guid>, ITenantOwned
{
    public const int NumberMaxLength = 50;
    public const int ReasonMaxLength = 500;
    public static readonly DateOnly EarliestIssueDate = new(2018, 1, 1);

    private readonly List<SupplierInvoiceLine> _lines = [];
    private readonly List<InvoiceHold> _holds = [];

    private SupplierInvoice(Guid id, Guid organizationId, Guid supplierId, Guid purchaseOrderId, InvoiceSource source) : base(id)
    {
        OrganizationId = organizationId;
        SupplierId = supplierId;
        PurchaseOrderId = purchaseOrderId;
        Source = source;
        SupplierInvoiceNumber = null!;
    }

    private SupplierInvoice()
    {
        SupplierInvoiceNumber = null!;
    }

    public Guid OrganizationId { get; private set; }

    public Guid SupplierId { get; private set; }

    public Guid PurchaseOrderId { get; private set; }

    /// <summary>The supplier's own number. Unique per supplier.</summary>
    public string SupplierInvoiceNumber { get; private set; }

    public DateOnly IssueDate { get; private set; }

    public InvoiceSource Source { get; private set; }

    public Guid? XmlFileId { get; private set; }

    public Guid? PdfFileId { get; private set; }

    /// <summary>The QR text as printed on the invoice (Base64 TLV), if any.</summary>
    public string? QrCode { get; private set; }

    public decimal NetTotal { get; private set; }

    public decimal VatTotal { get; private set; }

    public decimal Total { get; private set; }

    public InvoiceStatus Status { get; private set; }

    public DateTimeOffset ReceivedAtUtc { get; private set; }

    public Guid ReceivedBy { get; private set; }

    public DateTimeOffset? CheckedAtUtc { get; private set; }

    public DateTimeOffset? ReleasedAtUtc { get; private set; }

    public Guid? ReleasedBy { get; private set; }

    public string? RejectionReason { get; private set; }

    public IReadOnlyList<SupplierInvoiceLine> Lines => _lines;

    public IReadOnlyList<InvoiceHold> Holds => _holds;

    public IEnumerable<InvoiceHold> OpenHolds => _holds.Where(h => !h.IsOverridden);

    public static Result<SupplierInvoice> Receive(
        Guid organizationId, Guid supplierId, Guid purchaseOrderId, InvoiceDetails details, InvoiceSource source,
        Guid? xmlFileId, Guid? pdfFileId, Guid receivedBy, DateOnly today, DateTimeOffset now)
    {
        var number = details.SupplierInvoiceNumber?.Trim() ?? string.Empty;
        if (number.Length is 0 or > NumberMaxLength || details.IssueDate < EarliestIssueDate || details.IssueDate > today.AddDays(1)
            || details.Lines.Count == 0)
        {
            return PayablesErrors.DetailsInvalid;
        }

        if (details.Lines.Select(l => l.PoLineNumber).Distinct().Count() != details.Lines.Count
            || details.Lines.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || l.NetAmount < 0 || l.VatAmount < 0 || string.IsNullOrWhiteSpace(l.Description))
            || details.NetTotal < 0 || details.VatTotal < 0 || details.Total < 0)
        {
            return PayablesErrors.LinesInvalid;
        }

        var qr = string.IsNullOrWhiteSpace(details.QrCode) ? null : details.QrCode.Trim();
        if (qr?.Length > ZatcaQr.MaxLength)
        {
            return PayablesErrors.QrMalformed;
        }

        var invoice = new SupplierInvoice(Guid.CreateVersion7(), organizationId, supplierId, purchaseOrderId, source)
        {
            SupplierInvoiceNumber = number,
            IssueDate = details.IssueDate,
            XmlFileId = xmlFileId,
            PdfFileId = pdfFileId,
            QrCode = qr,
            NetTotal = details.NetTotal,
            VatTotal = details.VatTotal,
            Total = details.Total,
            Status = InvoiceStatus.Received,
            ReceivedAtUtc = now,
            ReceivedBy = receivedBy,
        };
        invoice._lines.AddRange(details.Lines.Select(l => new SupplierInvoiceLine(l)));
        invoice.Raise(new SupplierInvoiceReceived(invoice.Id, organizationId, supplierId, purchaseOrderId));
        return invoice;
    }

    /// <summary>
    /// Stores the result of a check. A hold that was overridden stays overridden if exactly the same
    /// problem (code, line, expected and actual values) is found again. No open holds means Matched.
    /// </summary>
    public Result ApplyCheck(IReadOnlyList<HoldFinding> findings, DateTimeOffset now)
    {
        if (Status is not (InvoiceStatus.Received or InvoiceStatus.OnHold or InvoiceStatus.Matched))
        {
            return PayablesErrors.InvalidStatusChange(Status, InvoiceStatus.OnHold);
        }

        var previous = _holds.ToList();
        _holds.Clear();
        foreach (var finding in findings.DistinctBy(f => (f.Code, f.LineNumber)))
        {
            var hold = new InvoiceHold(finding);
            var old = previous.FirstOrDefault(h => h.IsOverridden && h.Code == finding.Code && h.LineNumber == finding.LineNumber
                && h.Expected == finding.Expected && h.Actual == finding.Actual);
            if (old is not null)
            {
                hold.CopyOverride(old);
            }

            _holds.Add(hold);
        }

        CheckedAtUtc = now;
        UpdateMatchStatus();
        return Result.Success();
    }

    /// <summary>An AP clerk accepts a problem with a reason, for example a rounding difference on the QR.</summary>
    /// <param name="isAdmin">Only an admin can override a price difference.</param>
    public Result Override(string code, int? lineNumber, string reason, Guid by, bool isAdmin, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.OnHold)
        {
            return PayablesErrors.InvalidStatusChange(Status, InvoiceStatus.Matched);
        }

        var hold = OpenHolds.SingleOrDefault(h => h.Code == code && h.LineNumber == lineNumber);
        if (hold is null)
        {
            return PayablesErrors.HoldNotFound;
        }

        if (HoldCodes.NotOverridable.Contains(code))
        {
            return PayablesErrors.HoldNotOverridable;
        }

        if (HoldCodes.AdminOverride.Contains(code) && !isAdmin)
        {
            return PayablesErrors.OverrideNeedsAdmin;
        }

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 10 or > ReasonMaxLength)
        {
            return PayablesErrors.ReasonRequired;
        }

        hold.MarkOverridden(by, reason, now);
        UpdateMatchStatus();
        return Result.Success();
    }

    /// <summary>Releases a matched invoice for payment. The ERP pays it.</summary>
    public Result Release(Guid by, DateTimeOffset now)
    {
        if (Status != InvoiceStatus.Matched)
        {
            return PayablesErrors.InvalidStatusChange(Status, InvoiceStatus.ReleasedForPayment);
        }

        Status = InvoiceStatus.ReleasedForPayment;
        ReleasedAtUtc = now;
        ReleasedBy = by;
        Raise(new SupplierInvoiceReleased(Id, OrganizationId, SupplierId, PurchaseOrderId,
            _lines.Select(l => new InvoicedQuantity(l.PoLineNumber, l.Quantity)).ToList(), Total));
        return Result.Success();
    }

    public Result Reject(string reason)
    {
        if (Status is InvoiceStatus.ReleasedForPayment or InvoiceStatus.Rejected)
        {
            return PayablesErrors.InvalidStatusChange(Status, InvoiceStatus.Rejected);
        }

        reason = reason?.Trim() ?? string.Empty;
        if (reason.Length is < 10 or > ReasonMaxLength)
        {
            return PayablesErrors.ReasonRequired;
        }

        Status = InvoiceStatus.Rejected;
        RejectionReason = reason;
        Raise(new SupplierInvoiceRejected(Id, OrganizationId, SupplierId, reason));
        return Result.Success();
    }

    private void UpdateMatchStatus()
    {
        var next = OpenHolds.Any() ? InvoiceStatus.OnHold : InvoiceStatus.Matched;
        if (next == Status)
        {
            return;
        }

        Status = next;
        Raise(next == InvoiceStatus.Matched
            ? new SupplierInvoiceMatched(Id, OrganizationId)
            : new SupplierInvoiceHeld(Id, OrganizationId, OpenHolds.Select(h => h.Code).Distinct().ToList()));
    }
}

public enum InvoiceStatus
{
    Received = 1,
    Matched = 2,
    OnHold = 3,
    ReleasedForPayment = 4,
    Rejected = 5,
}

/// <summary>How the invoice data reached us.</summary>
public enum InvoiceSource
{
    /// <summary>The supplier uploaded a ZATCA UBL 2.1 XML file.</summary>
    UblXml = 1,

    /// <summary>An AP clerk typed it in from a PDF.</summary>
    Manual = 2,
}

/// <param name="SupplierInvoiceNumber">The supplier's invoice number.</param>
/// <param name="QrCode">The QR text (Base64 TLV), or null when the invoice has none.</param>
public sealed record InvoiceDetails(
    string SupplierInvoiceNumber, DateOnly IssueDate, IReadOnlyList<InvoiceLineInput> Lines, decimal NetTotal, decimal VatTotal, decimal Total, string? QrCode);

public sealed record InvoiceLineInput(
    int PoLineNumber, string Description, decimal Quantity, decimal UnitPrice, VatCategory VatCategory, decimal NetAmount, decimal VatAmount);

public sealed class SupplierInvoiceLine
{
    public const int DescriptionMaxLength = 500;

    internal SupplierInvoiceLine(InvoiceLineInput input)
    {
        Id = Guid.CreateVersion7();
        PoLineNumber = input.PoLineNumber;
        Description = input.Description.Trim().Length > DescriptionMaxLength ? input.Description.Trim()[..DescriptionMaxLength] : input.Description.Trim();
        Quantity = input.Quantity;
        UnitPrice = input.UnitPrice;
        VatCategory = input.VatCategory;
        NetAmount = input.NetAmount;
        VatAmount = input.VatAmount;
    }

    private SupplierInvoiceLine()
    {
        Description = null!;
    }

    public Guid Id { get; private set; }

    /// <summary>The PO line this invoice line bills for.</summary>
    public int PoLineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    public decimal UnitPrice { get; private set; }

    public VatCategory VatCategory { get; private set; }

    public decimal NetAmount { get; private set; }

    public decimal VatAmount { get; private set; }
}

/// <summary>One problem found by a check. <see cref="Expected"/> and <see cref="Actual"/> are for people to read.</summary>
public sealed record HoldFinding(string Code, int? LineNumber, string Expected, string Actual);

public sealed class InvoiceHold
{
    internal InvoiceHold(HoldFinding finding)
    {
        Id = Guid.CreateVersion7();
        Code = finding.Code;
        LineNumber = finding.LineNumber;
        Expected = finding.Expected;
        Actual = finding.Actual;
    }

    private InvoiceHold()
    {
        Code = null!;
        Expected = null!;
        Actual = null!;
    }

    public Guid Id { get; private set; }

    public string Code { get; private set; }

    /// <summary>The PO line number, for line-level problems.</summary>
    public int? LineNumber { get; private set; }

    public string Expected { get; private set; }

    public string Actual { get; private set; }

    public Guid? OverriddenBy { get; private set; }

    public string? OverrideReason { get; private set; }

    public DateTimeOffset? OverriddenAtUtc { get; private set; }

    public bool IsOverridden => OverriddenBy is not null;

    internal void MarkOverridden(Guid by, string reason, DateTimeOffset now)
    {
        OverriddenBy = by;
        OverrideReason = reason;
        OverriddenAtUtc = now;
    }

    internal void CopyOverride(InvoiceHold other) => MarkOverridden(other.OverriddenBy!.Value, other.OverrideReason!, other.OverriddenAtUtc!.Value);
}
