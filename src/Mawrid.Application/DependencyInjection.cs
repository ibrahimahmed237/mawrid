using FluentValidation;
using Mawrid.Application.Common.Behaviors;
using Microsoft.Extensions.DependencyInjection;

namespace Mawrid.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers handlers, validators and the request pipeline.
    /// Behaviors run in the order they are added: logging wraps validation.
    /// </summary>
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddMediatR(config =>
        {
            config.RegisterServicesFromAssembly(typeof(AssemblyReference).Assembly);
            config.AddOpenBehavior(typeof(LoggingBehavior<,>));
            config.AddOpenBehavior(typeof(ValidationBehavior<,>));
        });

        services.AddValidatorsFromAssembly(typeof(AssemblyReference).Assembly, includeInternalTypes: true);

        // Interfaces that one module publishes for other modules.
        services.AddScoped<Contracts.Files.IFileReferences, Files.FileReferences>();
        services.AddScoped<Contracts.Suppliers.ISupplierDirectory, Suppliers.SupplierDirectory>();
        services.AddScoped<Contracts.Identity.IOrganizationDirectory, Identity.OrganizationDirectory>();
        services.AddScoped<Contracts.Approvals.IApprovalService, Approvals.ApprovalService>();
        services.AddScoped<Contracts.Approvals.IApprovalDocumentHandler, Requisitions.RequisitionApprovalHandler>();
        services.AddScoped<Contracts.Approvals.IApprovalDocumentHandler, Purchasing.PurchaseOrderApprovalHandler>();
        services.AddScoped<Contracts.Requisitions.IRequisitionSourcing, Requisitions.RequisitionSourcing>();
        services.AddScoped<Contracts.Purchasing.IPurchaseOrderDrafts, Purchasing.PurchaseOrderDrafts>();
        services.AddScoped<Contracts.Purchasing.IPurchaseOrderInvoicing, Purchasing.PurchaseOrderInvoicing>();
        services.AddScoped<Payables.InvoiceChecks>();

        return services;
    }
}
