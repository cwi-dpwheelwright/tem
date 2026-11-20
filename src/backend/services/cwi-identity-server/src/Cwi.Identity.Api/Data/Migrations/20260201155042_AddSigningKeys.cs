using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cwi.Identity.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSigningKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "signing_keys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    KeyId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    EncryptedPrivateKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    Nonce = table.Column<byte[]>(type: "bytea", nullable: false),
                    PublicJwkJson = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_signing_keys", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_signing_keys_KeyId",
                table: "signing_keys",
                column: "KeyId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_signing_keys_Status",
                table: "signing_keys",
                column: "Status");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "signing_keys");
        }
    }
}
