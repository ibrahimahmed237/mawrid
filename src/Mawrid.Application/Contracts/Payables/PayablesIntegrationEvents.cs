using MediatR;

namespace Mawrid.Application.Contracts.Payables;

/// <summary>An invoice was released for payment. The ERP receives it by webhook and pays it.</summary>
public sealed record SupplierInvoiceReleasedIntegrationEvent(
    Guid OrganizationId,
    Guid InvoiceId,
    Guid SupplierId,
    string SupplierInvoiceNumber,
    DateOnly IssueDate,
    Guid PurchaseOrderId,
    string PurchaseOrderNumber,
    decimal NetTotal,
    decimal VatTotal,
    decimal Total) : INotification;
