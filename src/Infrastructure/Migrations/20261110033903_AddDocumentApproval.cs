using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ApprovedAt",
                table: "SourceDocuments",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "BillId",
                table: "SourceDocuments",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_SourceDocuments_BillId",
                table: "SourceDocuments",
                column: "BillId",
                unique: true,
                filter: "\"BillId\" IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_SourceDocuments_SupplierBills_BillId",
                table: "SourceDocuments",
                column: "BillId",
                principalTable: "SupplierBills",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SourceDocuments_SupplierBills_BillId",
                table: "SourceDocuments");

            migrationBuilder.DropIndex(
                name: "IX_SourceDocuments_BillId",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "BillId",
                table: "SourceDocuments");
        }
    }
}
