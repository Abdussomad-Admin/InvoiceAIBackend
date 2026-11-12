using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoicePublicPaymentTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PublicPaymentToken",
                table: "Invoices",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_PublicPaymentToken",
                table: "Invoices",
                column: "PublicPaymentToken",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Invoices_PublicPaymentToken",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "PublicPaymentToken",
                table: "Invoices");
        }
    }
}
