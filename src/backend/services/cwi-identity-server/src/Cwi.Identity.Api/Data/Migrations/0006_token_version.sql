START TRANSACTION;

ALTER TABLE users ADD COLUMN "TokenVersion" integer NOT NULL DEFAULT 0;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260201164705_AddTokenVersion', '8.0.8');

COMMIT;
