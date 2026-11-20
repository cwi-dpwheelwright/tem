CREATE TABLE IF NOT EXISTS operator_profiles (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  operator_id text NOT NULL,
  signals_total integer NOT NULL,
  corrections_total integer NOT NULL,
  delays_total integer NOT NULL,
  confirmations_total integer NOT NULL,
  latency_sum double precision NOT NULL,
  latency_count integer NOT NULL,
  correlation_score double precision NOT NULL,
  last_updated_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS ownership_lineage_bindings (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  asset_id text NOT NULL,
  owner_id text NOT NULL,
  relationship text NOT NULL,
  parent_asset_id text NULL,
  reason text NULL,
  created_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS succession_plans (
  id uuid PRIMARY KEY,
  plan_id text NOT NULL UNIQUE,
  tenant_id text NOT NULL,
  asset_id text NOT NULL,
  current_owner_id text NOT NULL,
  status text NOT NULL,
  successors_json jsonb NOT NULL,
  effective_utc timestamptz NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS succession_snapshots (
  id uuid PRIMARY KEY,
  snapshot_id text NOT NULL UNIQUE,
  tenant_id text NOT NULL,
  plan_id text NOT NULL,
  snapshot_json jsonb NOT NULL,
  created_utc timestamptz NOT NULL
);

CREATE TABLE IF NOT EXISTS artifact_registry (
  id uuid PRIMARY KEY,
  tenant_id text NOT NULL,
  artifact_id text NOT NULL,
  artifact_type text NOT NULL,
  title text NULL,
  status text NOT NULL,
  versions_json jsonb NOT NULL,
  created_utc timestamptz NOT NULL,
  updated_utc timestamptz NOT NULL
);

DO $$
BEGIN
  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'operator_profiles' AND column_name = 'tenant_id'
  ) AND EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'operator_profiles' AND column_name = 'operator_id'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS operator_profiles_tenant_operator_idx
      ON operator_profiles (tenant_id, operator_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'operator_profiles' AND column_name = 'TenantId'
  ) AND EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'operator_profiles' AND column_name = 'OperatorId'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS operator_profiles_tenant_operator_idx
      ON operator_profiles ("TenantId", "OperatorId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_lineage_bindings' AND column_name = 'tenant_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_lineage_bindings_tenant_id_idx
      ON ownership_lineage_bindings (tenant_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_lineage_bindings' AND column_name = 'TenantId'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_lineage_bindings_tenant_id_idx
      ON ownership_lineage_bindings ("TenantId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_lineage_bindings' AND column_name = 'asset_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_lineage_bindings_asset_id_idx
      ON ownership_lineage_bindings (asset_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'ownership_lineage_bindings' AND column_name = 'AssetId'
  ) THEN
    CREATE INDEX IF NOT EXISTS ownership_lineage_bindings_asset_id_idx
      ON ownership_lineage_bindings ("AssetId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'succession_plans' AND column_name = 'tenant_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS succession_plans_tenant_id_idx
      ON succession_plans (tenant_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'succession_plans' AND column_name = 'TenantId'
  ) THEN
    CREATE INDEX IF NOT EXISTS succession_plans_tenant_id_idx
      ON succession_plans ("TenantId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'succession_snapshots' AND column_name = 'tenant_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS succession_snapshots_tenant_id_idx
      ON succession_snapshots (tenant_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'succession_snapshots' AND column_name = 'TenantId'
  ) THEN
    CREATE INDEX IF NOT EXISTS succession_snapshots_tenant_id_idx
      ON succession_snapshots ("TenantId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'artifact_registry' AND column_name = 'tenant_id'
  ) AND EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'artifact_registry' AND column_name = 'artifact_id'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS artifact_registry_tenant_artifact_idx
      ON artifact_registry (tenant_id, artifact_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'artifact_registry' AND column_name = 'TenantId'
  ) AND EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'artifact_registry' AND column_name = 'ArtifactId'
  ) THEN
    CREATE UNIQUE INDEX IF NOT EXISTS artifact_registry_tenant_artifact_idx
      ON artifact_registry ("TenantId", "ArtifactId");
  END IF;

  IF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'artifact_registry' AND column_name = 'tenant_id'
  ) THEN
    CREATE INDEX IF NOT EXISTS artifact_registry_tenant_id_idx
      ON artifact_registry (tenant_id);
  ELSIF EXISTS (
    SELECT 1 FROM information_schema.columns
    WHERE table_name = 'artifact_registry' AND column_name = 'TenantId'
  ) THEN
    CREATE INDEX IF NOT EXISTS artifact_registry_tenant_id_idx
      ON artifact_registry ("TenantId");
  END IF;
END $$;
