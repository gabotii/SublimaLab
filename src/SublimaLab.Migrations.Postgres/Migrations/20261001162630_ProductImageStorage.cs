using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SublimaLab.Migrations.Postgres.Migrations
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
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "ProductImages",
                type: "character varying(200)",
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
