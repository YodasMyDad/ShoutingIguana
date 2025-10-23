using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoutingIguana.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    BaseUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastOpenedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    SettingsJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CrawlQueue",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Priority = table.Column<int>(type: "INTEGER", nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    HostKey = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    EnqueuedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlQueue", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrawlQueue_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Urls",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Address = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    NormalizedUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Scheme = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Host = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Path = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Depth = table.Column<int>(type: "INTEGER", nullable: false),
                    DiscoveredFromUrlId = table.Column<int>(type: "INTEGER", nullable: true),
                    FirstSeenUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastCrawledUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    HttpStatus = table.Column<int>(type: "INTEGER", nullable: true),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    ContentLength = table.Column<long>(type: "INTEGER", nullable: true),
                    RobotsAllowed = table.Column<bool>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Urls", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Urls_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Urls_Urls_DiscoveredFromUrlId",
                        column: x => x.DiscoveredFromUrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "Headers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 255, nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Headers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Headers_Urls_UrlId",
                        column: x => x.UrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromUrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToUrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    AnchorText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LinkType = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Links", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Links_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Links_Urls_FromUrlId",
                        column: x => x.FromUrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Links_Urls_ToUrlId",
                        column: x => x.ToUrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrawlQueue_HostKey",
                table: "CrawlQueue",
                column: "HostKey");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlQueue_ProjectId_State_Priority",
                table: "CrawlQueue",
                columns: new[] { "ProjectId", "State", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_Headers_UrlId",
                table: "Headers",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_FromUrlId",
                table: "Links",
                column: "FromUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_ProjectId_LinkType",
                table: "Links",
                columns: new[] { "ProjectId", "LinkType" });

            migrationBuilder.CreateIndex(
                name: "IX_Links_ToUrlId",
                table: "Links",
                column: "ToUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_Name",
                table: "Projects",
                column: "Name");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_Address",
                table: "Urls",
                column: "Address");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_DiscoveredFromUrlId",
                table: "Urls",
                column: "DiscoveredFromUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_Host",
                table: "Urls",
                column: "Host");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_NormalizedUrl",
                table: "Urls",
                column: "NormalizedUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_ProjectId_Status",
                table: "Urls",
                columns: new[] { "ProjectId", "Status" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrawlQueue");

            migrationBuilder.DropTable(
                name: "Headers");

            migrationBuilder.DropTable(
                name: "Links");

            migrationBuilder.DropTable(
                name: "Urls");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
