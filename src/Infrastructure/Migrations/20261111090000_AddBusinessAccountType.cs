using Microsoft.EntityFrameworkCore.Migrations;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;

#nullable disable

namespace Infrastructure.Migrations;

/// <inheritdoc />
[DbContext(typeof(AppDbContext))]
[Migration("20261111090000_AddBusinessAccountType")]
public partial class AddBusinessAccountType : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "AccountType",
            table: "Organizations",
            type: "integer",
            nullable: false,
            defaultValue: 1);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "AccountType", table: "Organizations");
}
