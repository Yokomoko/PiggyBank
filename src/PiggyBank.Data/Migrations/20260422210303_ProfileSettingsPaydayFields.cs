using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiggyBank.Data.Migrations
{
    /// <inheritdoc />
    public partial class ProfileSettingsPaydayFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PayPeriodStartDay",
                table: "ProfileSettings");

            migrationBuilder.AddColumn<bool>(
                name: "AdjustPaydayForWeekendsAndBankHolidays",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "PrimaryPaydayDayOfMonth",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: false,
                defaultValue: 25);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AdjustPaydayForWeekendsAndBankHolidays",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "PrimaryPaydayDayOfMonth",
                table: "ProfileSettings");

            migrationBuilder.AddColumn<int>(
                name: "PayPeriodStartDay",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: true);
        }
    }
}
