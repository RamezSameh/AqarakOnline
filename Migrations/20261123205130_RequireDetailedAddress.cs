using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqarakOnline.Migrations
{
    /// <inheritdoc />
    public partial class RequireDetailedAddress : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // الإعلانات القديمة اللي ملهاش عنوان تفصيلي (كان اختياري) — تتعبّى
            // بسلسلة فاضية قبل ما العمود يبقى NOT NULL، وإلا الـ migration هيفشل.
            migrationBuilder.Sql("UPDATE [Listings] SET [DetailedAddress] = '' WHERE [DetailedAddress] IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "DetailedAddress",
                table: "Listings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500,
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "DetailedAddress",
                table: "Listings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);
        }
    }
}
