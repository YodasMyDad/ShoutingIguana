using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoutingIguana.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddReportTables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ReportSchemas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    TaskKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    SchemaVersion = table.Column<int>(type: "INTEGER", nullable: false),
                    ColumnsJson = table.Column<string>(type: "TEXT", nullable: false),
                    IsUrlBased = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportSchemas", x => x.Id);
                    table.UniqueConstraint("AK_ReportSchemas_TaskKey", x => x.TaskKey);
                });

            migrationBuilder.CreateTable(
                name: "ReportRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    UrlId = table.Column<int>(type: "INTEGER", nullable: true),
                    RowDataJson = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReportRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReportRows_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReportRows_ReportSchemas_TaskKey",
                        column: x => x.TaskKey,
                        principalTable: "ReportSchemas",
                        principalColumn: "TaskKey",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ReportRows_Urls_UrlId",
                        column: x => x.UrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_CreatedUtc",
                table: "ReportRows",
                column: "CreatedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_ProjectId",
                table: "ReportRows",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_ProjectId_TaskKey",
                table: "ReportRows",
                columns: new[] { "ProjectId", "TaskKey" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_TaskKey",
                table: "ReportRows",
                column: "TaskKey");

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_UrlId",
                table: "ReportRows",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_ReportSchemas_TaskKey",
                table: "ReportSchemas",
                column: "TaskKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReportRows");

            migrationBuilder.DropTable(
                name: "ReportSchemas");
        }
    }
}
