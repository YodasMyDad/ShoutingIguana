using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ShoutingIguana.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompressRenderedHtml : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RenderedHtml",
                table: "Urls");

            migrationBuilder.AddColumn<byte[]>(
                name: "RenderedHtmlGzip",
                table: "Urls",
                type: "BLOB",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RenderedHtmlGzip",
                table: "Urls");

            migrationBuilder.AddColumn<string>(
                name: "RenderedHtml",
                table: "Urls",
                type: "TEXT",
                nullable: true);
        }
    }
}
