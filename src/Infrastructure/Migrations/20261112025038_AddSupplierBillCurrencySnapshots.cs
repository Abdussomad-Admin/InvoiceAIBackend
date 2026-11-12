using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSupplierBillCurrencySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseCurrencyCode",
                table: "SupplierBills",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "NGN");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "SupplierBills",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "BaseCurrencyCode",
                table: "BillPayments",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: false,
                defaultValue: "NGN");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "BillPayments",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.Sql("""
                UPDATE "SupplierBills" AS bill
                SET "BaseCurrencyCode" = organization."BaseCurrencyCode",
                    "ExchangeRateToBase" = 1
                FROM "Organizations" AS organization
                WHERE bill."OrganizationId" = organization."Id";

                UPDATE "BillPayments" AS payment
                SET "BaseCurrencyCode" = organization."BaseCurrencyCode",
                    "ExchangeRateToBase" = 1
                FROM "Organizations" AS organization
                WHERE payment."OrganizationId" = organization."Id";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseCurrencyCode",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "SupplierBills");

            migrationBuilder.DropColumn(
                name: "BaseCurrencyCode",
                table: "BillPayments");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "BillPayments");
        }
    }
}
