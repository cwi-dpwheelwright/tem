START TRANSACTION;

CREATE TABLE password_reset_tokens (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "UserId" uuid NOT NULL,
    "TokenHash" text NOT NULL,
    "IssuedUtc" timestamp with time zone NOT NULL,
    "ExpiresUtc" timestamp with time zone NOT NULL,
    "UsedUtc" timestamp with time zone,
    CONSTRAINT "PK_password_reset_tokens" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_password_reset_tokens_ExpiresUtc" ON password_reset_tokens ("ExpiresUtc");

CREATE INDEX "IX_password_reset_tokens_TenantId" ON password_reset_tokens ("TenantId");

CREATE UNIQUE INDEX "IX_password_reset_tokens_TokenHash" ON password_reset_tokens ("TokenHash");

CREATE INDEX "IX_password_reset_tokens_UsedUtc" ON password_reset_tokens ("UsedUtc");

CREATE INDEX "IX_password_reset_tokens_UserId" ON password_reset_tokens ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260201155548_AddPasswordResetTokens', '8.0.8');

COMMIT;

