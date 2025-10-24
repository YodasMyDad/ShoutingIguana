using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoutingIguana.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddStage2Tables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CanonicalUrl",
                table: "Urls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaDescription",
                table: "Urls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "MetaRobots",
                table: "Urls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RedirectTarget",
                table: "Urls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Title",
                table: "Urls",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Findings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    UrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    TaskKey = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: false),
                    Code = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Findings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Findings_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Findings_Urls_UrlId",
                        column: x => x.UrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Images",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    SrcUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    AltText = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    Width = table.Column<int>(type: "INTEGER", nullable: true),
                    Height = table.Column<int>(type: "INTEGER", nullable: true),
                    HttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Images", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Images_Urls_UrlId",
                        column: x => x.UrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Redirects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    ToUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Redirects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Redirects_Urls_UrlId",
                        column: x => x.UrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Findings_ProjectId",
                table: "Findings",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_Severity",
                table: "Findings",
                column: "Severity");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_TaskKey",
                table: "Findings",
                column: "TaskKey");

            migrationBuilder.CreateIndex(
                name: "IX_Findings_UrlId",
                table: "Findings",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Images_SrcUrl",
                table: "Images",
                column: "SrcUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Images_UrlId",
                table: "Images",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_UrlId",
                table: "Redirects",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_UrlId_Position",
                table: "Redirects",
                columns: new[] { "UrlId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Findings");

            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "Redirects");

            migrationBuilder.DropColumn(
                name: "CanonicalUrl",
                table: "Urls");

            migrationBuilder.DropColumn(
                name: "MetaDescription",
                table: "Urls");

            migrationBuilder.DropColumn(
                name: "MetaRobots",
                table: "Urls");

            migrationBuilder.DropColumn(
                name: "RedirectTarget",
                table: "Urls");

            migrationBuilder.DropColumn(
                name: "Title",
                table: "Urls");
        }
    }
}
