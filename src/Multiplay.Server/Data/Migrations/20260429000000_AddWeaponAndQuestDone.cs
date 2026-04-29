using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Multiplay.Server.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWeaponAndQuestDone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "WeaponType",
                table: "Users",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "SlimeQuestDone",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "WeaponType",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SlimeQuestDone",
                table: "Users");
        }
    }
}
