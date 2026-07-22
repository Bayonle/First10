using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class TriageAndMedia : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<Guid>(
                name: "TicketId",
                table: "timeline_entries",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ChallengeSentAt",
                table: "incident_tickets",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClassifierVersion",
                table: "incident_tickets",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Disposition",
                table: "incident_tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Evidence",
                table: "incident_tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Flags",
                table: "incident_tickets",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Language",
                table: "incident_tickets",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "media_assets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    PerceptualHash = table.Column<long>(type: "bigint", nullable: false),
                    TicketId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_media_assets", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "reporter_reputations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Channel = table.Column<int>(type: "integer", nullable: false),
                    ExternalUserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Trust = table.Column<int>(type: "integer", nullable: false),
                    Note = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_reporter_reputations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_incident_tickets_Disposition",
                table: "incident_tickets",
                column: "Disposition");

            migrationBuilder.CreateIndex(
                name: "IX_media_assets_CreatedAt",
                table: "media_assets",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_reporter_reputations_Channel_ExternalUserId",
                table: "reporter_reputations",
                columns: new[] { "Channel", "ExternalUserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "media_assets");

            migrationBuilder.DropTable(
                name: "reporter_reputations");

            migrationBuilder.DropIndex(
                name: "IX_incident_tickets_Disposition",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "ChallengeSentAt",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "ClassifierVersion",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "Disposition",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "Evidence",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "Flags",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "Language",
                table: "incident_tickets");

            migrationBuilder.AlterColumn<Guid>(
                name: "TicketId",
                table: "timeline_entries",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
