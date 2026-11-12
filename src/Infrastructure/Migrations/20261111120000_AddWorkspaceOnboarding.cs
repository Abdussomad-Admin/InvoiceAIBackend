using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261111120000_AddWorkspaceOnboarding")]
public partial class AddWorkspaceOnboarding : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
        => migrationBuilder.AddColumn<bool>(
            name: "IsOnboardingComplete",
            table: "Organizations",
            type: "boolean",
            nullable: false,
            defaultValue: true);

    protected override void Down(MigrationBuilder migrationBuilder)
        => migrationBuilder.DropColumn(name: "IsOnboardingComplete", table: "Organizations");
}
