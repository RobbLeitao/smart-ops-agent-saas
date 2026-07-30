using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SmartOps.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnosticFeedbackPerUser : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiagnosticFeedback",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DiagnosticId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    IsUseful = table.Column<bool>(type: "INTEGER", nullable: false),
                    VotedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiagnosticFeedback", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiagnosticFeedback_DiagnosticId_UserId",
                table: "DiagnosticFeedback",
                columns: new[] { "DiagnosticId", "UserId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiagnosticFeedback");
        }
    }
}
