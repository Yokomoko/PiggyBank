using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiggyBank.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoiceDefaultsAndSanitize : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "InvoiceCcEmails",
                table: "ProfileSettings",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceRecipientName",
                table: "ProfileSettings",
                type: "TEXT",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceSubjectPrefix",
                table: "ProfileSettings",
                type: "TEXT",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "InvoiceToEmails",
                table: "ProfileSettings",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "InvoiceCcEmails",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "InvoiceRecipientName",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "InvoiceSubjectPrefix",
                table: "ProfileSettings");

            migrationBuilder.DropColumn(
                name: "InvoiceToEmails",
                table: "ProfileSettings");
        }
    }
}
