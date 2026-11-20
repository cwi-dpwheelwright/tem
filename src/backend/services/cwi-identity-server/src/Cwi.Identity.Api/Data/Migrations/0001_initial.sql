CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

START TRANSACTION;

CREATE TABLE audit_events (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "UserId" uuid,
    "EventType" text NOT NULL,
    "PayloadJson" jsonb NOT NULL,
    "OccurredUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_audit_events" PRIMARY KEY ("Id")
);

CREATE TABLE memberships (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "UserId" uuid NOT NULL,
    "Status" text NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    "UpdatedUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_memberships" PRIMARY KEY ("Id")
);

CREATE TABLE refresh_tokens (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "UserId" uuid NOT NULL,
    "SessionId" uuid NOT NULL,
    "TokenHash" text NOT NULL,
    "IssuedUtc" timestamp with time zone NOT NULL,
    "ExpiresUtc" timestamp with time zone NOT NULL,
    "RevokedUtc" timestamp with time zone,
    CONSTRAINT "PK_refresh_tokens" PRIMARY KEY ("Id")
);

CREATE TABLE roles (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "Name" text NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_roles" PRIMARY KEY ("Id")
);

CREATE TABLE sessions (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "UserId" uuid NOT NULL,
    "Status" text NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    "ExpiresUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_sessions" PRIMARY KEY ("Id")
);

CREATE TABLE tenants (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "Name" text NOT NULL,
    "Status" text NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    "UpdatedUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_tenants" PRIMARY KEY ("Id")
);

CREATE TABLE user_roles (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "UserId" uuid NOT NULL,
    "RoleId" uuid NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_user_roles" PRIMARY KEY ("Id")
);

CREATE TABLE users (
    "Id" uuid NOT NULL,
    "TenantId" text NOT NULL,
    "Email" text NOT NULL,
    "DisplayName" character varying(200) NOT NULL,
    "Status" text NOT NULL,
    "PasswordHash" text NOT NULL,
    "CreatedUtc" timestamp with time zone NOT NULL,
    "UpdatedUtc" timestamp with time zone NOT NULL,
    CONSTRAINT "PK_users" PRIMARY KEY ("Id")
);

CREATE INDEX "IX_audit_events_EventType" ON audit_events ("EventType");

CREATE INDEX "IX_audit_events_OccurredUtc" ON audit_events ("OccurredUtc");

CREATE INDEX "IX_audit_events_TenantId" ON audit_events ("TenantId");

CREATE INDEX "IX_audit_events_UserId" ON audit_events ("UserId");

CREATE INDEX "IX_memberships_Status" ON memberships ("Status");

CREATE INDEX "IX_memberships_TenantId" ON memberships ("TenantId");

CREATE UNIQUE INDEX "IX_memberships_TenantId_UserId" ON memberships ("TenantId", "UserId");

CREATE INDEX "IX_refresh_tokens_ExpiresUtc" ON refresh_tokens ("ExpiresUtc");

CREATE INDEX "IX_refresh_tokens_SessionId" ON refresh_tokens ("SessionId");

CREATE INDEX "IX_refresh_tokens_TenantId" ON refresh_tokens ("TenantId");

CREATE UNIQUE INDEX "IX_refresh_tokens_TokenHash" ON refresh_tokens ("TokenHash");

CREATE INDEX "IX_refresh_tokens_UserId" ON refresh_tokens ("UserId");

CREATE UNIQUE INDEX "IX_roles_TenantId_Name" ON roles ("TenantId", "Name");

CREATE INDEX "IX_sessions_ExpiresUtc" ON sessions ("ExpiresUtc");

CREATE INDEX "IX_sessions_Status" ON sessions ("Status");

CREATE INDEX "IX_sessions_TenantId" ON sessions ("TenantId");

CREATE INDEX "IX_sessions_UserId" ON sessions ("UserId");

CREATE INDEX "IX_tenants_Status" ON tenants ("Status");

CREATE UNIQUE INDEX "IX_tenants_TenantId" ON tenants ("TenantId");

CREATE INDEX "IX_user_roles_TenantId" ON user_roles ("TenantId");

CREATE UNIQUE INDEX "IX_user_roles_TenantId_UserId_RoleId" ON user_roles ("TenantId", "UserId", "RoleId");

CREATE INDEX "IX_users_Email" ON users ("Email");

CREATE INDEX "IX_users_TenantId" ON users ("TenantId");

CREATE UNIQUE INDEX "IX_users_TenantId_Email" ON users ("TenantId", "Email");

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20260201151654_InitialIdentitySchema', '8.0.6');

COMMIT;

