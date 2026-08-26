using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrostBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class VideoNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<ulong>(
                name: "VideoNotificationChannelId",
                table: "GuildConfigs",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<ulong>(
                name: "VideoNotificationRoleId",
                table: "GuildConfigs",
                type: "INTEGER",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "VideoNotificationChannelId",
                table: "GuildConfigs");

            migrationBuilder.DropColumn(
                name: "VideoNotificationRoleId",
                table: "GuildConfigs");
        }
    }
}
