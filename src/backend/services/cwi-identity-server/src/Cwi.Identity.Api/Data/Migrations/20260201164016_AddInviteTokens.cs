using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cwi.Identity.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddInviteTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "invite_tokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<string>(type: "text", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Email = table.Column<string>(type: "text", nullable: false),
                    TokenHash = table.Column<string>(type: "text", nullable: false),
                    IssuedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UsedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_invite_tokens", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_invite_tokens_Email",
                table: "invite_tokens",
                column: "Email");

            migrationBuilder.CreateIndex(
                name: "IX_invite_tokens_ExpiresUtc",
                table: "invite_tokens",
                column: "ExpiresUtc");

            migrationBuilder.CreateIndex(
                name: "IX_invite_tokens_TenantId",
                table: "invite_tokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_invite_tokens_TokenHash",
                table: "invite_tokens",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_invite_tokens_UsedUtc",
                table: "invite_tokens",
                column: "UsedUtc");

            migrationBuilder.CreateIndex(
                name: "IX_invite_tokens_UserId",
                table: "invite_tokens",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "invite_tokens");
        }
    }
}
