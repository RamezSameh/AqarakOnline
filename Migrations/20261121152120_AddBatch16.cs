using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqarakOnline.Migrations
{
    /// <inheritdoc />
    public partial class AddBatch16 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ApartmentsCount",
                table: "Listings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ElevatorCount",
                table: "Listings",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "FeaturedUntil",
                table: "Listings",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OriginalPrice",
                table: "Listings",
                type: "decimal(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // الإعلانات الموجودة: سعرها الأصلي = سعرها الحالي (لا شارة تخفيض بأثر رجعي).
            migrationBuilder.Sql("UPDATE Listings SET OriginalPrice = Price");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApartmentsCount",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "ElevatorCount",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "FeaturedUntil",
                table: "Listings");

            migrationBuilder.DropColumn(
                name: "OriginalPrice",
                table: "Listings");
        }
    }
}
