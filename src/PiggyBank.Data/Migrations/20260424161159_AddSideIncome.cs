using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiggyBank.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSideIncome : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SideIncomeEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    PaidOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    DurationHours = table.Column<decimal>(type: "TEXT", nullable: true),
                    HourlyRate = table.Column<decimal>(type: "TEXT", nullable: true),
                    Total = table.Column<decimal>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SideIncomeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SideIncomeEntries_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SideIncomeAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    SideIncomeEntryId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Target = table.Column<int>(type: "INTEGER", nullable: false),
                    PocketId = table.Column<Guid>(type: "TEXT", nullable: true),
                    PocketDepositId = table.Column<Guid>(type: "TEXT", nullable: true),
                    MonthId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LedgerTransactionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    AllocatedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SideIncomeAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SideIncomeAllocations_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SideIncomeAllocations_SideIncomeEntries_SideIncomeEntryId",
                        column: x => x.SideIncomeEntryId,
                        principalTable: "SideIncomeEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SideIncomeAllocations_ProfileId_SideIncomeEntryId",
                table: "SideIncomeAllocations",
                columns: new[] { "ProfileId", "SideIncomeEntryId" });

            migrationBuilder.CreateIndex(
                name: "IX_SideIncomeAllocations_SideIncomeEntryId",
                table: "SideIncomeAllocations",
                column: "SideIncomeEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_SideIncomeEntries_ProfileId_PaidOn",
                table: "SideIncomeEntries",
                columns: new[] { "ProfileId", "PaidOn" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SideIncomeAllocations");

            migrationBuilder.DropTable(
                name: "SideIncomeEntries");
        }
    }
}
