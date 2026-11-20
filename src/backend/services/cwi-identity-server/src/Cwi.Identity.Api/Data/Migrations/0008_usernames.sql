START TRANSACTION;

ALTER TABLE users ADD COLUMN "Username" text;
CREATE INDEX "IX_users_Username" ON users ("Username");
CREATE UNIQUE INDEX "IX_users_TenantId_Username" ON users ("TenantId", "Username") WHERE "Username" IS NOT NULL;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260201210713_AddUsernames', '8.0.8');

COMMIT;
