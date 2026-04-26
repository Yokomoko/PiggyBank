using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiggyBank.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLedgerAndOutgoings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Months",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PeriodStart = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    PeriodEnd = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    LastPayday = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    NextPayday = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CarriedOverBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IsClosed = table.Column<bool>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Months", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "RecurringOutgoings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    DefaultAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsIncome = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsWage = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecurringOutgoings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MonthlyOutgoings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MonthId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RecurringOutgoingId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IsWage = table.Column<bool>(type: "INTEGER", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MonthlyOutgoings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MonthlyOutgoings_Months_MonthId",
                        column: x => x.MonthId,
                        principalTable: "Months",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    MonthId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Payee = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    CategoryId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    IsFuture = table.Column<bool>(type: "INTEGER", nullable: false),
                    ImportSource = table.Column<string>(type: "TEXT", maxLength: 60, nullable: true),
                    ImportRunId = table.Column<Guid>(type: "TEXT", nullable: true),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Transactions_Months_MonthId",
                        column: x => x.MonthId,
                        principalTable: "Months",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyOutgoings_MonthId",
                table: "MonthlyOutgoings",
                column: "MonthId");

            migrationBuilder.CreateIndex(
                name: "IX_MonthlyOutgoings_ProfileId_MonthId",
                table: "MonthlyOutgoings",
                columns: new[] { "ProfileId", "MonthId" });

            migrationBuilder.CreateIndex(
                name: "IX_Months_ProfileId_PeriodStart",
                table: "Months",
                columns: new[] { "ProfileId", "PeriodStart" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RecurringOutgoings_ProfileId_IsArchived_SortOrder",
                table: "RecurringOutgoings",
                columns: new[] { "ProfileId", "IsArchived", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_MonthId",
                table: "Transactions",
                column: "MonthId");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProfileId_CategoryId_Date",
                table: "Transactions",
                columns: new[] { "ProfileId", "CategoryId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProfileId_ImportRunId",
                table: "Transactions",
                columns: new[] { "ProfileId", "ImportRunId" });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_ProfileId_MonthId_Date",
                table: "Transactions",
                columns: new[] { "ProfileId", "MonthId", "Date" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MonthlyOutgoings");

            migrationBuilder.DropTable(
                name: "RecurringOutgoings");

            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Months");
        }
    }
}
