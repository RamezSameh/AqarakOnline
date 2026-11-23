using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqarakOnline.Migrations
{
    /// <inheritdoc />
    public partial class GuestAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ListingOffers_AspNetUsers_BuyerId",
                table: "ListingOffers");

            migrationBuilder.AlterColumn<string>(
                name: "RequesterId",
                table: "ViewingAppointments",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "RequesterName",
                table: "ViewingAppointments",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RequesterPhone",
                table: "ViewingAppointments",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestName",
                table: "Listings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GuestPhone",
                table: "Listings",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerId",
                table: "ListingOffers",
                type: "nvarchar(450)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");

            migrationBuilder.AddColumn<string>(
                name: "BuyerName",
                table: "ListingOffers",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerPhone",
                table: "ListingOffers",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ListingOffers_AspNetUsers_BuyerId",
                table: "ListingOffers",
                column: "BuyerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ListingOffers_AspNetUsers_BuyerId",
                table: "ListingOffers");

            migrationBuilder.DropColumn(
                name: "RequesterName",
                table: "ViewingAppointments");

            migrationBuilder.DropColumn(
                name: "RequesterPhone",
                table: "ViewingAppointments");

            migrationBuilder.DropColumn(
                name: "GuestName",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "GuestPhone",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "BuyerName",
                table: "ListingOffers");

            migrationBuilder.DropColumn(
                name: "BuyerPhone",
                table: "ListingOffers");

            migrationBuilder.AlterColumn<string>(
                name: "RequesterId",
                table: "ViewingAppointments",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BuyerId",
                table: "ListingOffers",
                type: "nvarchar(450)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(450)",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_ListingOffers_AspNetUsers_BuyerId",
                table: "ListingOffers",
                column: "BuyerId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
