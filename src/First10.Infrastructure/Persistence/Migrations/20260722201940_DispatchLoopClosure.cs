using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class DispatchLoopClosure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ArrivedAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Contradictions",
                table: "incident_tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CrewBriefing",
                table: "incident_tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Dispatch",
                table: "incident_tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "DispatchedAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Outcome",
                table: "incident_tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OutcomeAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SummarizerVersion",
                table: "incident_tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TimelineDigest",
                table: "incident_tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "TransportedAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ArrivedAt",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "Contradictions",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "CrewBriefing",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "Dispatch",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "DispatchedAt",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "Outcome",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "OutcomeAt",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "SummarizerVersion",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "TimelineDigest",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "TransportedAt",
                table: "incident_tickets");
        }
    }
}
