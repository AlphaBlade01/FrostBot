using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrostBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class RoleReward : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RoleRewards",
                columns: table => new
                {
                    RoleId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    LevelRequired = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoleRewards", x => x.RoleId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RoleRewards");
        }
    }
}
