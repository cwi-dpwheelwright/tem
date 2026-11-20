START TRANSACTION;

ALTER TABLE invite_tokens ADD COLUMN "ApprovedUtc" timestamp with time zone;
CREATE INDEX "IX_invite_tokens_ApprovedUtc" ON invite_tokens ("ApprovedUtc");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260201145959_AddInviteApproval', '8.0.8');

COMMIT;
