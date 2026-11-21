using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqarakOnline.Migrations
{
    /// <inheritdoc />
    public partial class AddBatch29 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ThumbPath",
                table: "ListingAttachments",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ThumbPath",
                table: "ListingAttachments");
        }
    }
}
