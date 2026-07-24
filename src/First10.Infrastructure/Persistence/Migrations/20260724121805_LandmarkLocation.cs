using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class LandmarkLocation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LocationLandmark",
                table: "incident_tickets",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LocationSource",
                table: "incident_tickets",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LocationLandmark",
                table: "incident_tickets");

            migrationBuilder.DropColumn(
                name: "LocationSource",
                table: "incident_tickets");
        }
    }
}
