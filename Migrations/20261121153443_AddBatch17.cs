using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AqarakOnline.Migrations
{
    /// <inheritdoc />
    public partial class AddBatch17 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ReminderSent",
                table: "ViewingAppointments",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReminderSent",
                table: "ViewingAppointments");
        }
    }
}
