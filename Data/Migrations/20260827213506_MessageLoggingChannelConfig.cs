using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrostBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class MessageLoggingChannelConfig : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsMessageLoggingDisabled",
                table: "ChannelConfigs",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsMessageLoggingDisabled",
                table: "ChannelConfigs");
        }
    }
}
