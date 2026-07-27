using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrostBot.Data.Migrations
{
    /// <inheritdoc />
    public partial class MessageLogging : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Messages",
                columns: table => new
                {
                    MessageId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    AuthorId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    ChannelId = table.Column<ulong>(type: "INTEGER", nullable: false),
                    Content = table.Column<string>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Messages", x => x.MessageId);
                });
            migrationBuilder.Sql(@"
                CREATE TRIGGER IF NOT EXISTS LimitMessageLogs
                AFTER INSERT ON Messages
                BEGIN
                    DELETE FROM Messages
                    WHERE MessageId IN (
                        SELECT MessageId FROM Messages
                        ORDER BY CreatedAt ASC
                        LIMIT MAX(0, (SELECT COUNT(*) FROM Messages) - 20000)
                    );
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Messages");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS LimitMessageLogs;");
        }
    }
}
