using Mawrid.Domain.Common;

namespace Mawrid.Domain.Sourcing;

/// <summary>
/// A supplier's priced answer to an RFQ. One per supplier and RFQ. The supplier can revise it
/// until the deadline; only the latest version counts.
/// </summary>
public sealed class Quotation : AggregateRoot<Guid>, ITenantOwned
{
    public const int NotesMaxLength = 1000;

    private readonly List<QuotationLine> _lines = [];

    private Quotation(Guid id, Guid organizationId, Guid rfqId, Guid supplierId) : base(id)
    {
        OrganizationId = organizationId;
        RfqId = rfqId;
        SupplierId = supplierId;
        AttachmentFileIds = [];
    }

    private Quotation()
    {
        AttachmentFileIds = [];
    }

    public Guid OrganizationId { get; private set; }

    public Guid RfqId { get; private set; }

    public Guid SupplierId { get; private set; }

    public DateOnly ValidUntil { get; private set; }

    public string? Notes { get; private set; }

    /// <summary>How many times the supplier saved it (1 = first submission).</summary>
    public int Version { get; private set; }

    /// <summary>The RFQ revision this quotation answers. Lower than the RFQ's revision means "re-confirm".</summary>
    public int RfqRevision { get; private set; }

    public DateTimeOffset SubmittedAtUtc { get; private set; }

    public List<Guid> AttachmentFileIds { get; private set; }

    public IReadOnlyList<QuotationLine> Lines => _lines;

    /// <summary>Submits (first time) or revises (later) the quotation while the RFQ is open.</summary>
    public static Result<Quotation> Submit(Rfq rfq, Guid supplierId, QuotationInput input, DateTimeOffset now, DateOnly today)
    {
        var quotation = new Quotation(Guid.CreateVersion7(), rfq.OrganizationId, rfq.Id, supplierId);
        var result = quotation.Revise(rfq, input, now, today);
        return result.IsSuccess ? quotation : result.Error!;
    }

    public Result Revise(Rfq rfq, QuotationInput input, DateTimeOffset now, DateOnly today)
    {
        // The deadline is checked here, with the real clock, whatever the RFQ status column says.
        if (!rfq.IsOpen(now))
        {
            return SourcingErrors.NotOpen;
        }

        if (!rfq.IsInvited(SupplierId))
        {
            return SourcingErrors.NotInvited;
        }

        if (input.ValidUntil <= today)
        {
            return SourcingErrors.ValidityInvalid;
        }

        if (input.Lines.Count == 0 || input.Lines.Select(l => l.ItemLineNumber).Distinct().Count() != input.Lines.Count)
        {
            return SourcingErrors.QuotationLinesInvalid;
        }

        foreach (var line in input.Lines)
        {
            if (rfq.Items.All(i => i.LineNumber != line.ItemLineNumber) || line.UnitPrice <= 0 || line.LeadTimeDays is < 0 or > 365
                || decimal.Round(line.UnitPrice, 4) != line.UnitPrice)
            {
                return SourcingErrors.QuotationLinesInvalid;
            }
        }

        var notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();
        if (notes?.Length > NotesMaxLength)
        {
            return SourcingErrors.QuotationLinesInvalid;
        }

        _lines.Clear();
        _lines.AddRange(input.Lines.Select(l => new QuotationLine(l.ItemLineNumber, l.UnitPrice, l.VatCategory, l.LeadTimeDays)));
        ValidUntil = input.ValidUntil;
        Notes = notes;
        AttachmentFileIds = input.AttachmentFileIds.Distinct().ToList();
        Version++;
        RfqRevision = rfq.Revision;
        SubmittedAtUtc = now;
        Raise(new QuotationSubmitted(Id, OrganizationId, RfqId, SupplierId, Version));
        return Result.Success();
    }

    public bool NeedsReconfirmation(Rfq rfq) => RfqRevision < rfq.Revision;
}

public sealed record QuotationInput(DateOnly ValidUntil, IReadOnlyList<QuotationLineInput> Lines, string? Notes, IReadOnlyList<Guid> AttachmentFileIds);

public sealed record QuotationLineInput(int ItemLineNumber, decimal UnitPrice, VatCategory VatCategory, int LeadTimeDays);

/// <summary>The supplier's price for one RFQ item, without VAT. Items left out are "no bid".</summary>
public sealed class QuotationLine
{
    internal QuotationLine(int itemLineNumber, decimal unitPrice, VatCategory vatCategory, int leadTimeDays)
    {
        ItemLineNumber = itemLineNumber;
        UnitPrice = unitPrice;
        VatCategory = vatCategory;
        LeadTimeDays = leadTimeDays;
    }

    private QuotationLine()
    {
    }

    public int ItemLineNumber { get; private set; }

    public decimal UnitPrice { get; private set; }

    public VatCategory VatCategory { get; private set; }

    public int LeadTimeDays { get; private set; }
}
