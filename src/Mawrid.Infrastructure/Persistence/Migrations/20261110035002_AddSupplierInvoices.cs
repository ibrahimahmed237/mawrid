using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Mawrid.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "payables");

            migrationBuilder.CreateTable(
                name: "SupplierInvoices",
                schema: "payables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PurchaseOrderId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SupplierInvoiceNumber = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    IssueDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    XmlFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    PdfFileId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    QrCode = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NetTotal = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    VatTotal = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Total = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    ReceivedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    ReceivedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    CheckedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    ReleasedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RejectionReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    CreatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    UpdatedBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoiceHolds",
                schema: "payables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Code = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    LineNumber = table.Column<int>(type: "int", nullable: true),
                    Expected = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Actual = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    OverriddenBy = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OverrideReason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    OverriddenAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    SupplierInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoiceHolds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierInvoiceHolds_SupplierInvoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalSchema: "payables",
                        principalTable: "SupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SupplierInvoiceLines",
                schema: "payables",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    PoLineNumber = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Quantity = table.Column<decimal>(type: "decimal(18,3)", precision: 18, scale: 3, nullable: false),
                    UnitPrice = table.Column<decimal>(type: "decimal(19,4)", precision: 19, scale: 4, nullable: false),
                    VatCategory = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    NetAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "decimal(19,2)", precision: 19, scale: 2, nullable: false),
                    SupplierInvoiceId = table.Column<Guid>(type: "uniqueidentifier", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SupplierInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SupplierInvoiceLines_SupplierInvoices_SupplierInvoiceId",
                        column: x => x.SupplierInvoiceId,
                        principalSchema: "payables",
                        principalTable: "SupplierInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceHolds_SupplierInvoiceId",
                schema: "payables",
                table: "SupplierInvoiceHolds",
                column: "SupplierInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoiceLines_SupplierInvoiceId",
                schema: "payables",
                table: "SupplierInvoiceLines",
                column: "SupplierInvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_OrganizationId_PurchaseOrderId",
                schema: "payables",
                table: "SupplierInvoices",
                columns: new[] { "OrganizationId", "PurchaseOrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_OrganizationId_Status",
                schema: "payables",
                table: "SupplierInvoices",
                columns: new[] { "OrganizationId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SupplierInvoices_OrganizationId_SupplierId_SupplierInvoiceNumber",
                schema: "payables",
                table: "SupplierInvoices",
                columns: new[] { "OrganizationId", "SupplierId", "SupplierInvoiceNumber" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SupplierInvoiceHolds",
                schema: "payables");

            migrationBuilder.DropTable(
                name: "SupplierInvoiceLines",
                schema: "payables");

            migrationBuilder.DropTable(
                name: "SupplierInvoices",
                schema: "payables");
        }
    }
}
