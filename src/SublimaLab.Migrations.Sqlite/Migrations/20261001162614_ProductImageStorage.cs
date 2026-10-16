using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SublimaLab.Migrations.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ProductImageStorage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ContentType",
                table: "ProductImages",
                type: "TEXT",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "ProductImages",
                type: "TEXT",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentType",
                table: "ProductImages");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "ProductImages");
        }
    }
}
