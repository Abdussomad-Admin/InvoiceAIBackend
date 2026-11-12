using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScheduleAndCreditNoteCurrencySnapshots : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BaseCurrencyCode",
                table: "RecurringInvoiceSchedules",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "RecurringInvoiceSchedules",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.AddColumn<string>(
                name: "BaseCurrencyCode",
                table: "CreditNotes",
                type: "character(3)",
                fixedLength: true,
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRateToBase",
                table: "CreditNotes",
                type: "numeric(18,8)",
                precision: 18,
                scale: 8,
                nullable: false,
                defaultValue: 1m);

            migrationBuilder.Sql("""
                UPDATE "RecurringInvoiceSchedules" AS s
                SET "BaseCurrencyCode" = o."BaseCurrencyCode"
                FROM "Organizations" AS o
                WHERE o."Id" = s."OrganizationId";
                UPDATE "CreditNotes" AS c
                SET "BaseCurrencyCode" = i."BaseCurrencyCode",
                    "ExchangeRateToBase" = i."ExchangeRateToBase"
                FROM "Invoices" AS i
                WHERE i."Id" = c."InvoiceId";
                """);
            migrationBuilder.AlterColumn<string>(name: "BaseCurrencyCode", table: "RecurringInvoiceSchedules",
                type: "character(3)", fixedLength: true, maxLength: 3, nullable: false,
                oldClrType: typeof(string), oldType: "character(3)", oldFixedLength: true,
                oldMaxLength: 3, oldNullable: true);
            migrationBuilder.AlterColumn<string>(name: "BaseCurrencyCode", table: "CreditNotes",
                type: "character(3)", fixedLength: true, maxLength: 3, nullable: false,
                oldClrType: typeof(string), oldType: "character(3)", oldFixedLength: true,
                oldMaxLength: 3, oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BaseCurrencyCode",
                table: "RecurringInvoiceSchedules");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "RecurringInvoiceSchedules");

            migrationBuilder.DropColumn(
                name: "BaseCurrencyCode",
                table: "CreditNotes");

            migrationBuilder.DropColumn(
                name: "ExchangeRateToBase",
                table: "CreditNotes");
        }
    }
}
