using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace First10.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class BlurAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "blur_audits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MediaRef = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    FacesDetected = table.Column<int>(type: "integer", nullable: false),
                    LowConfidenceRegions = table.Column<int>(type: "integer", nullable: false),
                    MinConfidence = table.Column<double>(type: "double precision", nullable: true),
                    Fallback = table.Column<int>(type: "integer", nullable: false),
                    DurationMs = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_blur_audits", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_blur_audits_CreatedAt",
                table: "blur_audits",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_blur_audits_MediaRef",
                table: "blur_audits",
                column: "MediaRef");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "blur_audits");
        }
    }
}
