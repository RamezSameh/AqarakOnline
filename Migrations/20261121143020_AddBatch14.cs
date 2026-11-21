using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqarakOnline.Migrations
{
    /// <inheritdoc />
    public partial class AddBatch14 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FurnishedOnly",
                table: "SavedSearches");

            migrationBuilder.AddColumn<int>(
                name: "Furnishing",
                table: "Listings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            // نقل البيانات القديمة قبل حذف العمود: المفروش = كامل (2)، غير المفروش = بلا (0).
            migrationBuilder.Sql("UPDATE Listings SET Furnishing = 2 WHERE IsFurnished = 1");

            migrationBuilder.DropColumn(
                name: "IsFurnished",
                table: "Listings");

            migrationBuilder.AddColumn<string>(
                name: "Furnishing",
                table: "SavedSearches",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SaleReason",
                table: "Listings",
                type: "nvarchar(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Furnishing",
                table: "SavedSearches");

            migrationBuilder.DropColumn(
                name: "Furnishing",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "SaleReason",
                table: "Listings");

            migrationBuilder.AddColumn<bool>(
                name: "FurnishedOnly",
                table: "SavedSearches",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFurnished",
                table: "Listings",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }
    }
}
