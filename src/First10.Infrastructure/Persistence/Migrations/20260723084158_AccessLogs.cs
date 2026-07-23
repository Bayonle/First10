using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccessLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "access_logs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    Who = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    MediaRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_access_logs", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_access_logs_At",
                table: "access_logs",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_access_logs_TicketId",
                table: "access_logs",
                column: "TicketId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "access_logs");
        }
    }
}
