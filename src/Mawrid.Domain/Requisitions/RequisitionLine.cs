using Mawrid.Domain.Common;

namespace Mawrid.Domain.Requisitions;

public sealed class RequisitionLine
{
    public const int DescriptionMaxLength = 500;
    public const int UnitMaxLength = 20;

    private RequisitionLine(int lineNumber, string description, decimal quantity, string unit, decimal estimatedUnitPrice, Guid? preferredSupplierId)
    {
        Id = Guid.CreateVersion7();
        LineNumber = lineNumber;
        Description = description;
        Quantity = quantity;
        Unit = unit;
        EstimatedUnitPrice = estimatedUnitPrice;
        PreferredSupplierId = preferredSupplierId;
    }

    private RequisitionLine()
    {
        Description = null!;
        Unit = null!;
    }

    /// <summary>Own key: editing a draft replaces all lines, and new lines must be inserted, not updated.</summary>
    public Guid Id { get; private set; }

    public int LineNumber { get; private set; }

    public string Description { get; private set; }

    public decimal Quantity { get; private set; }

    /// <summary>Unit of measure, such as "pcs", "box" or "kg".</summary>
    public string Unit { get; private set; }

    /// <summary>Estimated price per unit in SAR, without VAT.</summary>
    public decimal EstimatedUnitPrice { get; private set; }

    public Guid? PreferredSupplierId { get; private set; }

    public decimal EstimatedNetAmount => Math.Round(Quantity * EstimatedUnitPrice, 2, MidpointRounding.AwayFromZero);

    internal static Result<RequisitionLine> Create(int lineNumber, RequisitionLineInput input)
    {
        var description = input.Description?.Trim() ?? string.Empty;
        var unit = input.Unit?.Trim() ?? string.Empty;

        if (description.Length is < 2 or > DescriptionMaxLength || unit.Length is < 1 or > UnitMaxLength)
        {
            return RequisitionErrors.LineInvalid(lineNumber);
        }

        if (input.Quantity <= 0 || input.EstimatedUnitPrice < 0 || decimal.Round(input.Quantity, 3) != input.Quantity
            || decimal.Round(input.EstimatedUnitPrice, 4) != input.EstimatedUnitPrice)
        {
            return RequisitionErrors.LineInvalid(lineNumber);
        }

        return new RequisitionLine(lineNumber, description, input.Quantity, unit, input.EstimatedUnitPrice, input.PreferredSupplierId);
    }
}
