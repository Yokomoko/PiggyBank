using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiggyBank.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantForeignKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddForeignKey(
                name: "FK_MonthlyOutgoings_Profiles_ProfileId",
                table: "MonthlyOutgoings",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Months_Profiles_ProfileId",
                table: "Months",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_RecurringOutgoings_Profiles_ProfileId",
                table: "RecurringOutgoings",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);

            migrationBuilder.AddForeignKey(
                name: "FK_Transactions_Profiles_ProfileId",
                table: "Transactions",
                column: "ProfileId",
                principalTable: "Profiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_MonthlyOutgoings_Profiles_ProfileId",
                table: "MonthlyOutgoings");

            migrationBuilder.DropForeignKey(
                name: "FK_Months_Profiles_ProfileId",
                table: "Months");

            migrationBuilder.DropForeignKey(
                name: "FK_RecurringOutgoings_Profiles_ProfileId",
                table: "RecurringOutgoings");

            migrationBuilder.DropForeignKey(
                name: "FK_Transactions_Profiles_ProfileId",
                table: "Transactions");
        }
    }
}
