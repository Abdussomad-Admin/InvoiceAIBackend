using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SupportMultiplePaystackCurrencies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationPaystackAccounts_OrganizationId",
                table: "OrganizationPaystackAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPaystackAccounts_OrganizationId_CurrencyCode",
                table: "OrganizationPaystackAccounts",
                columns: new[] { "OrganizationId", "CurrencyCode" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrganizationPaystackAccounts_OrganizationId_CurrencyCode",
                table: "OrganizationPaystackAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_OrganizationPaystackAccounts_OrganizationId",
                table: "OrganizationPaystackAccounts",
                column: "OrganizationId",
                unique: true);
        }
    }
}
