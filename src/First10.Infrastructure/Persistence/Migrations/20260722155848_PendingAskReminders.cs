using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class PendingAskReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastReminderSentAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReminderSentAt",
                table: "incident_tickets");
        }
    }
}
