START TRANSACTION;

CREATE TABLE signing_keys (
    "Id" uuid NOT NULL,
    "KeyId" text NOT NULL,
    "Status" text NOT NULL,
    "EncryptedPrivateKey" bytea NOT NULL,
    "Nonce" bytea NOT NULL,
    "PublicJwkJson" jsonb NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_signing_keys" PRIMARY KEY ("Id")
);

CREATE UNIQUE INDEX "IX_signing_keys_KeyId" ON signing_keys ("KeyId");

CREATE INDEX "IX_signing_keys_Status" ON signing_keys ("Status");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260201155042_AddSigningKeys', '8.0.8');

COMMIT;

