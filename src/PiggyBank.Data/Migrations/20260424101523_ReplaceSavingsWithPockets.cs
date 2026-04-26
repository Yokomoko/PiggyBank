using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiggyBank.Data.Migrations
{
    /// <inheritdoc />
    public partial class ReplaceSavingsWithPockets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SavingsProjections");

            migrationBuilder.CreateTable(
                name: "Deposits",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DepositedOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    RecordedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deposits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Deposits_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Pockets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    CurrentBalance = table.Column<decimal>(type: "TEXT", nullable: false),
                    AutoSavePercent = table.Column<decimal>(type: "TEXT", nullable: false),
                    Goal = table.Column<decimal>(type: "TEXT", nullable: true),
                    AnnualInterestRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Pockets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Pockets_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DepositAllocations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    DepositId = table.Column<Guid>(type: "TEXT", nullable: false),
                    PocketId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AutoSavePercentAtDeposit = table.Column<decimal>(type: "TEXT", nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepositAllocations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DepositAllocations_Deposits_DepositId",
                        column: x => x.DepositId,
                        principalTable: "Deposits",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepositAllocations_Pockets_PocketId",
                        column: x => x.PocketId,
                        principalTable: "Pockets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DepositAllocations_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepositAllocations_DepositId",
                table: "DepositAllocations",
                column: "DepositId");

            migrationBuilder.CreateIndex(
                name: "IX_DepositAllocations_PocketId",
                table: "DepositAllocations",
                column: "PocketId");

            migrationBuilder.CreateIndex(
                name: "IX_DepositAllocations_ProfileId_DepositId",
                table: "DepositAllocations",
                columns: new[] { "ProfileId", "DepositId" });

            migrationBuilder.CreateIndex(
                name: "IX_DepositAllocations_ProfileId_PocketId",
                table: "DepositAllocations",
                columns: new[] { "ProfileId", "PocketId" });

            migrationBuilder.CreateIndex(
                name: "IX_Deposits_ProfileId_DepositedOn",
                table: "Deposits",
                columns: new[] { "ProfileId", "DepositedOn" });

            migrationBuilder.CreateIndex(
                name: "IX_Pockets_ProfileId_ArchivedAtUtc_SortOrder",
                table: "Pockets",
                columns: new[] { "ProfileId", "ArchivedAtUtc", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DepositAllocations");

            migrationBuilder.DropTable(
                name: "Deposits");

            migrationBuilder.DropTable(
                name: "Pockets");

            migrationBuilder.CreateTable(
                name: "SavingsProjections",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AnnualInterestRate = table.Column<decimal>(type: "TEXT", nullable: false),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    MonthlyContribution = table.Column<decimal>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    OpeningDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    OpeningValue = table.Column<decimal>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SavingsProjections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SavingsProjections_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SavingsProjections_ProfileId_ArchivedAtUtc_SortOrder",
                table: "SavingsProjections",
                columns: new[] { "ProfileId", "ArchivedAtUtc", "SortOrder" });
        }
    }
}
