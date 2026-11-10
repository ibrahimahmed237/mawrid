using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Mawrid.Api.Common.OpenApi;

/// <summary>The OpenAPI document: title, description and the JWT bearer scheme.</summary>
public static class OpenApiSetup
{
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services) =>
        services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
        {
            document.Info.Title = "Mawrid API";
            document.Info.Description =
                "Buyer-side procure-to-pay for Saudi Arabia: requisitions, approvals, sealed RFQs, purchase orders, " +
                "goods receipts and supplier invoices with ZATCA QR checks and a three-way match. " +
                "Log in with POST /api/v1/auth/login and send the access token as a Bearer token.";

            document.Components ??= new OpenApiComponents();
            document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
            document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                Description = "The access token from POST /api/v1/auth/login (valid 15 minutes).",
            };
            document.Security = [new OpenApiSecurityRequirement { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
            return Task.CompletedTask;
        }));
}
