START TRANSACTION;

CREATE TABLE invite_tokens (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "UserId" uuid NOT NULL,
    "Email" text NOT NULL,
    "TokenHash" text NOT NULL,
    "IssuedUtc" timestamp with time zone NOT NULL,
    "ExpiresUtc" timestamp with time zone NOT NULL,
    "UsedUtc" timestamp with time zone,
    CONSTRAINT "PK_invite_tokens" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_invite_tokens_Email" ON invite_tokens ("Email");

CREATE INDEX "IX_invite_tokens_ExpiresUtc" ON invite_tokens ("ExpiresUtc");

CREATE INDEX "IX_invite_tokens_TenantId" ON invite_tokens ("TenantId");

CREATE UNIQUE INDEX "IX_invite_tokens_TokenHash" ON invite_tokens ("TokenHash");

CREATE INDEX "IX_invite_tokens_UsedUtc" ON invite_tokens ("UsedUtc");

CREATE INDEX "IX_invite_tokens_UserId" ON invite_tokens ("UserId");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260201164016_AddInviteTokens', '8.0.8');

COMMIT;
