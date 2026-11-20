using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cwi.Identity.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInviteApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedUtc",
                table: "invite_tokens",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_invite_tokens_ApprovedUtc",
                table: "invite_tokens",
                column: "ApprovedUtc");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_invite_tokens_ApprovedUtc",
                table: "invite_tokens");

            migrationBuilder.DropColumn(
                name: "ApprovedUtc",
                table: "invite_tokens");
        }
    }
}
