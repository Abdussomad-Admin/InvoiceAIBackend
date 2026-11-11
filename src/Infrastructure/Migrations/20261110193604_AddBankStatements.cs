using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBankStatements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BankStatementBatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    FileHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ImportedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportedLineCount = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementBatches_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BankStatementLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    StatementBatchId = table.Column<Guid>(type: "uuid", nullable: false),
                    RowNumber = table.Column<int>(type: "integer", nullable: false),
                    TransactionDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false),
                    Reference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    MatchedPaymentId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReconciledByUserId = table.Column<Guid>(type: "uuid", nullable: true),
                    ResolutionNote = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BankStatementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_BankStatementBatches_StatementBatchId",
                        column: x => x.StatementBatchId,
                        principalTable: "BankStatementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BankStatementLines_Payments_MatchedPaymentId",
                        column: x => x.MatchedPaymentId,
                        principalTable: "Payments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementBatches_OrganizationId_CreatedAt",
                table: "BankStatementBatches",
                columns: new[] { "OrganizationId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementBatches_OrganizationId_FileHash",
                table: "BankStatementBatches",
                columns: new[] { "OrganizationId", "FileHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_MatchedPaymentId",
                table: "BankStatementLines",
                column: "MatchedPaymentId");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_OrganizationId_MatchedPaymentId",
                table: "BankStatementLines",
                columns: new[] { "OrganizationId", "MatchedPaymentId" },
                unique: true,
                filter: "\"MatchedPaymentId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_OrganizationId_StatementBatchId_RowNumber",
                table: "BankStatementLines",
                columns: new[] { "OrganizationId", "StatementBatchId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_OrganizationId_TransactionDate_Status",
                table: "BankStatementLines",
                columns: new[] { "OrganizationId", "TransactionDate", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_BankStatementLines_StatementBatchId",
                table: "BankStatementLines",
                column: "StatementBatchId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BankStatementLines");

            migrationBuilder.DropTable(
                name: "BankStatementBatches");
        }
    }
}
