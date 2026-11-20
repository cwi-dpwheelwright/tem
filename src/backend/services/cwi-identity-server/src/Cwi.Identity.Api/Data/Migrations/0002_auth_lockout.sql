START TRANSACTION;

ALTER TABLE users ADD "FailedLoginCount" integer NOT NULL DEFAULT 0;

ALTER TABLE users ADD "LockoutUntilUtc" timestamp with time zone;

CREATE INDEX "IX_users_LockoutUntilUtc" ON users ("LockoutUntilUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260201153215_AddAuthLockoutFields', '8.0.8');

COMMIT;

