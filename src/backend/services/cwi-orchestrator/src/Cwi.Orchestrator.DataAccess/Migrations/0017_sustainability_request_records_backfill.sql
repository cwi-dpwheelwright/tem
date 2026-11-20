ALTER TABLE sustainability_request_records
    ADD COLUMN IF NOT EXISTS node_watts double precision NULL,
    ADD COLUMN IF NOT EXISTS watts_source text NOT NULL DEFAULT 'unknown',
    ADD COLUMN IF NOT EXISTS prometheus_instance text NULL,
    ADD COLUMN IF NOT EXISTS telemetry_source text NOT NULL DEFAULT 'unknown',
    ADD COLUMN IF NOT EXISTS calculation_version text NOT NULL DEFAULT 'unknown';

WITH latest_sustainability_events AS (
    SELECT DISTINCT ON (request_id)
        request_id,
        metadata_json
    FROM journey_events
    WHERE component = 'sustainability_request'
      AND request_id IS NOT NULL
      AND metadata_json IS NOT NULL
    ORDER BY request_id, created_utc DESC, id DESC
),
parsed AS (
    SELECT
        request_id,
        CASE WHEN metadata_json->>'energy_wh' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'energy_wh')::double precision END AS energy_wh,
        CASE WHEN metadata_json->>'cost_usd' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'cost_usd')::double precision END AS cost_usd,
        CASE WHEN metadata_json->>'water_liters' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'water_liters')::double precision END AS water_liters,
        CASE WHEN metadata_json->>'cloud_baseline_energy_wh' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'cloud_baseline_energy_wh')::double precision END AS cloud_baseline_energy_wh,
        CASE WHEN metadata_json->>'cloud_baseline_cost_usd' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'cloud_baseline_cost_usd')::double precision END AS cloud_baseline_cost_usd,
        CASE WHEN metadata_json->>'cloud_baseline_water_liters' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'cloud_baseline_water_liters')::double precision END AS cloud_baseline_water_liters,
        CASE WHEN metadata_json->>'energy_saved_wh' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'energy_saved_wh')::double precision END AS energy_saved_wh,
        CASE WHEN metadata_json->>'cost_saved_usd' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'cost_saved_usd')::double precision END AS cost_saved_usd,
        CASE WHEN metadata_json->>'water_saved_liters' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'water_saved_liters')::double precision END AS water_saved_liters,
        CASE WHEN metadata_json->>'node_watts' ~ '^[+-]?([0-9]+([.][0-9]*)?|[.][0-9]+)([eE][+-]?[0-9]+)?$'
            THEN (metadata_json->>'node_watts')::double precision END AS node_watts,
        NULLIF(metadata_json->>'value_type', '') AS value_type,
        NULLIF(metadata_json->>'energy_value_type', '') AS energy_value_type,
        NULLIF(metadata_json->>'cost_value_type', '') AS cost_value_type,
        NULLIF(metadata_json->>'water_value_type', '') AS water_value_type,
        NULLIF(metadata_json->>'savings_value_type', '') AS savings_value_type,
        NULLIF(metadata_json->>'baseline_model_version', '') AS baseline_model_version,
        NULLIF(metadata_json->>'calculation_version', '') AS calculation_version,
        NULLIF(metadata_json->>'node_id', '') AS node_id,
        NULLIF(metadata_json->>'node_name', '') AS node_name,
        NULLIF(metadata_json->>'node_group', '') AS node_group,
        NULLIF(metadata_json->>'node_role', '') AS node_role,
        NULLIF(metadata_json->>'watts_source', '') AS watts_source,
        NULLIF(metadata_json->>'prometheus_instance', '') AS prometheus_instance,
        NULLIF(metadata_json->>'telemetry_source', '') AS telemetry_source
    FROM latest_sustainability_events
)
UPDATE sustainability_request_records AS records
SET
    energy_wh = COALESCE(records.energy_wh, parsed.energy_wh),
    energy_value_type = CASE
        WHEN records.energy_wh IS NULL AND parsed.energy_wh IS NOT NULL
            THEN COALESCE(parsed.energy_value_type, parsed.value_type, records.energy_value_type, 'estimated')
        ELSE records.energy_value_type
    END,
    cost_usd = COALESCE(records.cost_usd, parsed.cost_usd),
    cost_value_type = CASE
        WHEN records.cost_usd IS NULL AND parsed.cost_usd IS NOT NULL
            THEN COALESCE(parsed.cost_value_type, parsed.value_type, records.cost_value_type, 'estimated')
        ELSE records.cost_value_type
    END,
    water_liters = COALESCE(records.water_liters, parsed.water_liters),
    water_value_type = CASE
        WHEN records.water_liters IS NULL AND parsed.water_liters IS NOT NULL
            THEN COALESCE(parsed.water_value_type, parsed.value_type, records.water_value_type, 'estimated')
        ELSE records.water_value_type
    END,
    cloud_baseline_energy_wh = COALESCE(records.cloud_baseline_energy_wh, parsed.cloud_baseline_energy_wh),
    cloud_baseline_energy_value_type = CASE
        WHEN records.cloud_baseline_energy_wh IS NULL AND parsed.cloud_baseline_energy_wh IS NOT NULL
            THEN COALESCE(parsed.energy_value_type, parsed.value_type, records.cloud_baseline_energy_value_type, 'estimated')
        ELSE records.cloud_baseline_energy_value_type
    END,
    cloud_baseline_cost_usd = COALESCE(records.cloud_baseline_cost_usd, parsed.cloud_baseline_cost_usd),
    cloud_baseline_cost_value_type = CASE
        WHEN records.cloud_baseline_cost_usd IS NULL AND parsed.cloud_baseline_cost_usd IS NOT NULL
            THEN COALESCE(parsed.cost_value_type, parsed.value_type, records.cloud_baseline_cost_value_type, 'estimated')
        ELSE records.cloud_baseline_cost_value_type
    END,
    cloud_baseline_water_liters = COALESCE(records.cloud_baseline_water_liters, parsed.cloud_baseline_water_liters),
    cloud_baseline_water_value_type = CASE
        WHEN records.cloud_baseline_water_liters IS NULL AND parsed.cloud_baseline_water_liters IS NOT NULL
            THEN COALESCE(parsed.water_value_type, parsed.value_type, records.cloud_baseline_water_value_type, 'estimated')
        ELSE records.cloud_baseline_water_value_type
    END,
    energy_saved_wh = COALESCE(records.energy_saved_wh, parsed.energy_saved_wh),
    cost_saved_usd = COALESCE(records.cost_saved_usd, parsed.cost_saved_usd),
    water_saved_liters = COALESCE(records.water_saved_liters, parsed.water_saved_liters),
    savings_value_type = CASE
        WHEN (records.energy_saved_wh IS NULL AND parsed.energy_saved_wh IS NOT NULL)
          OR (records.cost_saved_usd IS NULL AND parsed.cost_saved_usd IS NOT NULL)
          OR (records.water_saved_liters IS NULL AND parsed.water_saved_liters IS NOT NULL)
            THEN COALESCE(parsed.savings_value_type, parsed.value_type, records.savings_value_type, 'estimated')
        ELSE records.savings_value_type
    END,
    baseline_model_version = COALESCE(parsed.baseline_model_version, records.baseline_model_version),
    node_watts = COALESCE(records.node_watts, parsed.node_watts),
    watts_source = CASE
        WHEN records.watts_source = 'unknown' THEN COALESCE(parsed.watts_source, records.watts_source)
        ELSE records.watts_source
    END,
    prometheus_instance = COALESCE(records.prometheus_instance, parsed.prometheus_instance),
    telemetry_source = CASE
        WHEN records.telemetry_source = 'unknown' THEN COALESCE(parsed.telemetry_source, records.telemetry_source)
        ELSE records.telemetry_source
    END,
    calculation_version = CASE
        WHEN records.calculation_version = 'unknown' THEN COALESCE(parsed.calculation_version, records.calculation_version)
        ELSE records.calculation_version
    END,
    node_id = CASE
        WHEN records.node_id IN ('', 'unknown')
          OR (records.node_id = 'shared_compute' AND parsed.node_id IS NOT NULL AND parsed.node_id <> 'shared_compute')
            THEN COALESCE(parsed.node_id, records.node_id)
        ELSE records.node_id
    END,
    node_name = CASE
        WHEN records.node_name IN ('', 'unknown') THEN COALESCE(parsed.node_name, records.node_name)
        ELSE records.node_name
    END,
    node_group = CASE
        WHEN records.node_group IN ('', 'unknown') THEN COALESCE(parsed.node_group, records.node_group)
        ELSE records.node_group
    END,
    node_role = CASE
        WHEN records.node_role IN ('', 'unknown') THEN COALESCE(parsed.node_role, records.node_role)
        ELSE records.node_role
    END
FROM parsed
WHERE records.request_id = parsed.request_id
  AND (
      records.energy_wh IS NULL
      OR records.cost_usd IS NULL
      OR records.water_liters IS NULL
      OR records.cloud_baseline_energy_wh IS NULL
      OR records.cloud_baseline_cost_usd IS NULL
      OR records.cloud_baseline_water_liters IS NULL
      OR records.energy_saved_wh IS NULL
      OR records.cost_saved_usd IS NULL
      OR records.water_saved_liters IS NULL
      OR records.node_watts IS NULL
      OR records.watts_source = 'unknown'
      OR records.telemetry_source = 'unknown'
      OR records.calculation_version = 'unknown'
  );
