using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ReporterFeedback : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "AckSentAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LocationRequestSentAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LocationResolvedAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "LastCannedReplyAt",
                table: "conversations",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AckSentAt",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "LocationRequestSentAt",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "LocationResolvedAt",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "LastCannedReplyAt",
                table: "conversations");
        }
    }
}
