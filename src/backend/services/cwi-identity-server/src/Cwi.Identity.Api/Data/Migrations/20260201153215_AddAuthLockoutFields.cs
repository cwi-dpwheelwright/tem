using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cwi.Identity.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAuthLockoutFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "FailedLoginCount",
                table: "users",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "LockoutUntilUtc",
                table: "users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_users_LockoutUntilUtc",
                table: "users",
                column: "LockoutUntilUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_users_LockoutUntilUtc",
                table: "users");

            migrationBuilder.DropColumn(
                name: "FailedLoginCount",
                table: "users");

            migrationBuilder.DropColumn(
                name: "LockoutUntilUtc",
                table: "users");
        }
    }
}
