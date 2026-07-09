using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace VmTips.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddRulesTextToAppSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RulesText",
                table: "AppSettings",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RulesText",
                table: "AppSettings");
        }
    }
}
