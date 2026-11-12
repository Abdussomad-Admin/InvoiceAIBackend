using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceExchangeRateSnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "Payments",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "BaseCurrencyCode",
                table: "Invoices",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "Invoices",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.Sql("""
                UPDATE "Invoices" AS i
                SET "BaseCurrencyCode" = o."BaseCurrencyCode"
                FROM "Organizations" AS o
                WHERE o."Id" = i."OrganizationId";
                UPDATE "Payments" AS p
                SET "ExchangeRateToBase" = i."ExchangeRateToBase"
                FROM "Invoices" AS i
                WHERE i."Id" = p."InvoiceId";
                """);
            migrationBuilder.AlterColumn<string>(name: "BaseCurrencyCode", table: "Invoices",
                type: "character(3)", fixedLength: true, maxLength: 3, nullable: false,
                oldClrType: typeof(string), oldType: "character(3)", oldFixedLength: true,
                oldMaxLength: 3, oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "Payments");

            migrationBuilder.DropColumn(
                name: "BaseCurrencyCode",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "Invoices");
        }
    }
}
