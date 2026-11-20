using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqarakOnline.Migrations
{
    /// <inheritdoc />
    public partial class AddListingDetails : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AdvertiserType",
                table: "Listings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "Bathrooms",
                table: "Listings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Floor",
                table: "Listings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsFurnished",
                table: "Listings",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Rooms",
                table: "Listings",
                type: "int",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdvertiserType",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "Bathrooms",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "Floor",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "IsFurnished",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "Rooms",
                table: "Listings");
        }
    }
}
