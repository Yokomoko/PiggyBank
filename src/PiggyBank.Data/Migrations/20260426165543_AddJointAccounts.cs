using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PiggyBank.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddJointAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JointAccounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    BankName = table.Column<string>(type: "TEXT", maxLength: 80, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    ArchivedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JointAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "JointContributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JointAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProfileId = table.Column<Guid>(type: "TEXT", nullable: false),
                    MonthlyAmount = table.Column<decimal>(type: "TEXT", nullable: false),
                    ModifiedAtUtc = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JointContributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JointContributions_JointAccounts_JointAccountId",
                        column: x => x.JointAccountId,
                        principalTable: "JointAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_JointContributions_Profiles_ProfileId",
                        column: x => x.ProfileId,
                        principalTable: "Profiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "JointOutgoings",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    JointAccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 120, nullable: false),
                    Amount = table.Column<decimal>(type: "TEXT", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", nullable: true),
                    SortOrder = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JointOutgoings", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JointOutgoings_JointAccounts_JointAccountId",
                        column: x => x.JointAccountId,
                        principalTable: "JointAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JointAccounts_ArchivedAtUtc_SortOrder",
                table: "JointAccounts",
                columns: new[] { "ArchivedAtUtc", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_JointContributions_JointAccountId",
                table: "JointContributions",
                column: "JointAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_JointContributions_ProfileId",
                table: "JointContributions",
                column: "ProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_JointOutgoings_JointAccountId_SortOrder",
                table: "JointOutgoings",
                columns: new[] { "JointAccountId", "SortOrder" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JointContributions");

            migrationBuilder.DropTable(
                name: "JointOutgoings");

            migrationBuilder.DropTable(
                name: "JointAccounts");
        }
    }
}
