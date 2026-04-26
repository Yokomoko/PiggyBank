using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiggyBank.Data.Migrations
{
    /// <inheritdoc />
    public partial class RemoveTransactionIsFutureAndAddTaxBand : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsFuture",
                table: "Transactions");

            migrationBuilder.AddColumn<int>(
                name: "SideIncomeTaxBand",
                table: "ProfileSettings",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "SideIncomeTaxCustomRate",
                table: "ProfileSettings",
                type: "TEXT",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SideIncomeTaxBand",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "SideIncomeTaxCustomRate",
                table: "ProfileSettings");

            migrationBuilder.AddColumn<bool>(
                name: "IsFuture",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
