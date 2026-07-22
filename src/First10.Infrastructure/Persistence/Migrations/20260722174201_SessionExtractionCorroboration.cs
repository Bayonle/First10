using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SessionExtractionCorroboration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "TranscriptText",
                table: "timeline_entries",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CasualtyEstimate",
                table: "incident_tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExtractorVersion",
                table: "incident_tickets",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "InstructionSentAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LocationLat",
                table: "incident_tickets",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "LocationLng",
                table: "incident_tickets",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ReporterCount",
                table: "incident_tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Severity",
                table: "incident_tickets",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "micro_instruction_templates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Language = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Text = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    AudioMediaRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false),
                    ApprovedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_micro_instruction_templates", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_micro_instruction_templates_Key_Language_Version",
                table: "micro_instruction_templates",
                columns: new[] { "Key", "Language", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "micro_instruction_templates");

            migrationBuilder.DropColumn(
                name: "TranscriptText",
                table: "timeline_entries");

            migrationBuilder.DropColumn(
                name: "CasualtyEstimate",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "ExtractorVersion",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "InstructionSentAt",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "LocationLat",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "LocationLng",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "ReporterCount",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "incident_tickets");
        }
    }
}
