using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoutingIguana.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
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
                name: "CrawlCheckpoints",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Phase = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    UrlsCrawled = table.Column<int>(type: "INTEGER", nullable: false),
                    UrlsAnalyzed = table.Column<int>(type: "INTEGER", nullable: false),
                    ErrorCount = table.Column<int>(type: "INTEGER", nullable: false),
                    QueueSize = table.Column<int>(type: "INTEGER", nullable: false),
                    LastCrawledUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    Status = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ElapsedSeconds = table.Column<double>(type: "REAL", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrawlCheckpoints", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CrawlCheckpoints_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "CustomExtractionRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FieldName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    SelectorType = table.Column<int>(type: "INTEGER", nullable: false),
                    Selector = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false, defaultValue: true),
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomExtractionRules", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomExtractionRules_Projects_ProjectId",
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
                    RobotsAllowed = table.Column<bool>(type: "INTEGER", nullable: true),
                    Title = table.Column<string>(type: "TEXT", nullable: true),
                    MetaDescription = table.Column<string>(type: "TEXT", nullable: true),
                    CanonicalUrl = table.Column<string>(type: "TEXT", nullable: true),
                    MetaRobots = table.Column<string>(type: "TEXT", nullable: true),
                    RedirectTarget = table.Column<string>(type: "TEXT", nullable: true),
                    CanonicalHtml = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    CanonicalHttp = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    HasMultipleCanonicals = table.Column<bool>(type: "INTEGER", nullable: false),
                    HasCrossDomainCanonical = table.Column<bool>(type: "INTEGER", nullable: false),
                    CanonicalIssues = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    RobotsNoindex = table.Column<bool>(type: "INTEGER", nullable: true),
                    RobotsNofollow = table.Column<bool>(type: "INTEGER", nullable: true),
                    RobotsNoarchive = table.Column<bool>(type: "INTEGER", nullable: true),
                    RobotsNosnippet = table.Column<bool>(type: "INTEGER", nullable: true),
                    RobotsNoimageindex = table.Column<bool>(type: "INTEGER", nullable: true),
                    RobotsSource = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    XRobotsTag = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    HasRobotsConflict = table.Column<bool>(type: "INTEGER", nullable: false),
                    HtmlLang = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    ContentLanguageHeader = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    HasMetaRefresh = table.Column<bool>(type: "INTEGER", nullable: false),
                    MetaRefreshDelay = table.Column<int>(type: "INTEGER", nullable: true),
                    MetaRefreshTarget = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    HasJsChanges = table.Column<bool>(type: "INTEGER", nullable: false),
                    JsChangedElements = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    IsRedirectLoop = table.Column<bool>(type: "INTEGER", nullable: false),
                    RedirectChainLength = table.Column<int>(type: "INTEGER", nullable: true),
                    IsSoft404 = table.Column<bool>(type: "INTEGER", nullable: false),
                    CacheControl = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Vary = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    ContentEncoding = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LinkHeader = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true),
                    HasHsts = table.Column<bool>(type: "INTEGER", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: true),
                    SimHash = table.Column<long>(type: "INTEGER", nullable: true),
                    IsIndexable = table.Column<bool>(type: "INTEGER", nullable: true),
                    RenderedHtml = table.Column<string>(type: "TEXT", nullable: true)
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
                name: "Hreflangs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    LanguageCode = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    TargetUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IsXDefault = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hreflangs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Hreflangs_Urls_UrlId",
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
                name: "Links",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ProjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    FromUrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    ToUrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    AnchorText = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    LinkType = table.Column<int>(type: "INTEGER", nullable: false),
                    RelAttribute = table.Column<string>(type: "TEXT", maxLength: 255, nullable: true),
                    IsNofollow = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsUgc = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSponsored = table.Column<bool>(type: "INTEGER", nullable: false),
                    DomPath = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ElementTag = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    IsVisible = table.Column<bool>(type: "INTEGER", nullable: true),
                    PositionX = table.Column<int>(type: "INTEGER", nullable: true),
                    PositionY = table.Column<int>(type: "INTEGER", nullable: true),
                    ElementWidth = table.Column<int>(type: "INTEGER", nullable: true),
                    ElementHeight = table.Column<int>(type: "INTEGER", nullable: true),
                    HtmlSnippet = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ParentTag = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true)
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
                    CreatedUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Severity = table.Column<int>(type: "INTEGER", nullable: true),
                    IssueText = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "StructuredData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UrlId = table.Column<int>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    SchemaType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    RawData = table.Column<string>(type: "TEXT", nullable: false),
                    IsValid = table.Column<bool>(type: "INTEGER", nullable: false),
                    ValidationErrors = table.Column<string>(type: "TEXT", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_StructuredData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_StructuredData_Urls_UrlId",
                        column: x => x.UrlId,
                        principalTable: "Urls",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CrawlCheckpoints_ProjectId_IsActive",
                table: "CrawlCheckpoints",
                columns: new[] { "ProjectId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_CrawlQueue_HostKey",
                table: "CrawlQueue",
                column: "HostKey");

            migrationBuilder.CreateIndex(
                name: "IX_CrawlQueue_ProjectId_Address",
                table: "CrawlQueue",
                columns: new[] { "ProjectId", "Address" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CrawlQueue_ProjectId_State_Priority",
                table: "CrawlQueue",
                columns: new[] { "ProjectId", "State", "Priority" });

            migrationBuilder.CreateIndex(
                name: "IX_CustomExtractionRules_ProjectId",
                table: "CustomExtractionRules",
                column: "ProjectId");

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
                name: "IX_Headers_UrlId",
                table: "Headers",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Hreflangs_IsXDefault",
                table: "Hreflangs",
                column: "IsXDefault");

            migrationBuilder.CreateIndex(
                name: "IX_Hreflangs_LanguageCode",
                table: "Hreflangs",
                column: "LanguageCode");

            migrationBuilder.CreateIndex(
                name: "IX_Hreflangs_UrlId",
                table: "Hreflangs",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Hreflangs_UrlId_LanguageCode",
                table: "Hreflangs",
                columns: new[] { "UrlId", "LanguageCode" });

            migrationBuilder.CreateIndex(
                name: "IX_Images_SrcUrl",
                table: "Images",
                column: "SrcUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Images_UrlId",
                table: "Images",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_FromUrlId",
                table: "Links",
                column: "FromUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Links_IsNofollow",
                table: "Links",
                column: "IsNofollow");

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
                name: "IX_Redirects_UrlId",
                table: "Redirects",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Redirects_UrlId_Position",
                table: "Redirects",
                columns: new[] { "UrlId", "Position" });

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
                name: "IX_ReportRows_ProjectId_TaskKey_IssueText",
                table: "ReportRows",
                columns: new[] { "ProjectId", "TaskKey", "IssueText" });

            migrationBuilder.CreateIndex(
                name: "IX_ReportRows_ProjectId_TaskKey_Severity_Id",
                table: "ReportRows",
                columns: new[] { "ProjectId", "TaskKey", "Severity", "Id" });

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

            migrationBuilder.CreateIndex(
                name: "IX_StructuredData_IsValid",
                table: "StructuredData",
                column: "IsValid");

            migrationBuilder.CreateIndex(
                name: "IX_StructuredData_SchemaType",
                table: "StructuredData",
                column: "SchemaType");

            migrationBuilder.CreateIndex(
                name: "IX_StructuredData_Type",
                table: "StructuredData",
                column: "Type");

            migrationBuilder.CreateIndex(
                name: "IX_StructuredData_UrlId",
                table: "StructuredData",
                column: "UrlId");

            migrationBuilder.CreateIndex(
                name: "IX_StructuredData_UrlId_Type",
                table: "StructuredData",
                columns: new[] { "UrlId", "Type" });

            migrationBuilder.CreateIndex(
                name: "IX_Urls_Address",
                table: "Urls",
                column: "Address");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_ContentHash",
                table: "Urls",
                column: "ContentHash");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_DiscoveredFromUrlId",
                table: "Urls",
                column: "DiscoveredFromUrlId");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_HasMultipleCanonicals",
                table: "Urls",
                column: "HasMultipleCanonicals");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_Host",
                table: "Urls",
                column: "Host");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_HtmlLang",
                table: "Urls",
                column: "HtmlLang");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_IsIndexable",
                table: "Urls",
                column: "IsIndexable");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_NormalizedUrl",
                table: "Urls",
                column: "NormalizedUrl");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_ProjectId_Status",
                table: "Urls",
                columns: new[] { "ProjectId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Urls_RobotsNoindex",
                table: "Urls",
                column: "RobotsNoindex");

            migrationBuilder.CreateIndex(
                name: "IX_Urls_SimHash",
                table: "Urls",
                column: "SimHash");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CrawlCheckpoints");

            migrationBuilder.DropTable(
                name: "CrawlQueue");

            migrationBuilder.DropTable(
                name: "CustomExtractionRules");

            migrationBuilder.DropTable(
                name: "Findings");

            migrationBuilder.DropTable(
                name: "Headers");

            migrationBuilder.DropTable(
                name: "Hreflangs");

            migrationBuilder.DropTable(
                name: "Images");

            migrationBuilder.DropTable(
                name: "Links");

            migrationBuilder.DropTable(
                name: "Redirects");

            migrationBuilder.DropTable(
                name: "ReportRows");

            migrationBuilder.DropTable(
                name: "StructuredData");

            migrationBuilder.DropTable(
                name: "ReportSchemas");

            migrationBuilder.DropTable(
                name: "Urls");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
