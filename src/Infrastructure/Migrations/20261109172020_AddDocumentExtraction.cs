using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDocumentExtraction : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ExtractedData",
                table: "SourceDocuments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ExtractionAttempts",
                table: "SourceDocuments",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LastError",
                table: "SourceDocuments",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DocumentExtractions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    DocumentId = table.Column<Guid>(type: "uuid", nullable: false),
                    AttemptNumber = table.Column<int>(type: "integer", nullable: false),
                    RequestedByUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Model = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    Succeeded = table.Column<bool>(type: "boolean", nullable: false),
                    DurationMs = table.Column<int>(type: "integer", nullable: false),
                    IssueCount = table.Column<int>(type: "integer", nullable: false),
                    ErrorMessage = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    RawOutput = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DocumentExtractions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DocumentExtractions_Organizations_OrganizationId",
                        column: x => x.OrganizationId,
                        principalTable: "Organizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentExtractions_SourceDocuments_DocumentId",
                        column: x => x.DocumentId,
                        principalTable: "SourceDocuments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DocumentExtractions_Users_RequestedByUserId",
                        column: x => x.RequestedByUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExtractions_DocumentId_AttemptNumber",
                table: "DocumentExtractions",
                columns: new[] { "DocumentId", "AttemptNumber" });

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExtractions_OrganizationId",
                table: "DocumentExtractions",
                column: "OrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_DocumentExtractions_RequestedByUserId",
                table: "DocumentExtractions",
                column: "RequestedByUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DocumentExtractions");

            migrationBuilder.DropColumn(
                name: "ExtractedData",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "ExtractionAttempts",
                table: "SourceDocuments");

            migrationBuilder.DropColumn(
                name: "LastError",
                table: "SourceDocuments");
        }
    }
}
