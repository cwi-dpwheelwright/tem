DO $$
DECLARE
    r RECORD;
    newname TEXT;
BEGIN
    FOR r IN
        SELECT table_name, column_name
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name IN (
            'tenant_config',
            'sessions',
            'approval_tickets',
            'ledger_events',
            'ledger_outbox',
            'telemetry_windows',
            'connector_runs',
            'break_glass_tokens',
            'pack_activation_states',
            'canonical_records',
            'ownership_transfer_requests',
            'outcome_feedback',
            'operator_profiles',
            'ownership_lineage_bindings',
            'succession_plans',
            'succession_snapshots',
            'artifact_registry'
          )
    LOOP
        newname := regexp_replace(initcap(replace(r.column_name, '_', ' ')), '\s', '', 'g');
        IF r.column_name <> newname THEN
            EXECUTE format('ALTER TABLE %I RENAME COLUMN %I TO %I', r.table_name, r.column_name, newname);
        END IF;
    END LOOP;
END $$;
