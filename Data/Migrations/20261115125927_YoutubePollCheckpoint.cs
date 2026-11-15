using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrostBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class YoutubePollCheckpoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YoutubePollStates",
                columns: table => new
                {
                    ChannelId = table.Column<string>(type: "TEXT", nullable: false),
                    LastVideoId = table.Column<string>(type: "TEXT", nullable: true),
                    LastPublishedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YoutubePollStates", x => x.ChannelId);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YoutubePollStates");
        }
    }
}
