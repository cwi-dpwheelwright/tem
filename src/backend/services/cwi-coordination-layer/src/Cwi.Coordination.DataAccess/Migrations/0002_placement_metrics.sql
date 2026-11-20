CREATE TABLE IF NOT EXISTS coordination_placement_metrics (
  id uuid PRIMARY KEY,
  metrics_id text NOT NULL,
  node_id text NOT NULL,
  observed_utc timestamptz NOT NULL,
  scenario_id text NULL,
  radio jsonb NOT NULL,
  environment jsonb NOT NULL,
  uncertainty jsonb NOT NULL,
  metadata jsonb NULL
);

CREATE UNIQUE INDEX IF NOT EXISTS coordination_placement_metrics_metrics_id_idx
  ON coordination_placement_metrics (metrics_id);

CREATE INDEX IF NOT EXISTS coordination_placement_metrics_node_id_idx
  ON coordination_placement_metrics (node_id);

CREATE INDEX IF NOT EXISTS coordination_placement_metrics_observed_utc_idx
  ON coordination_placement_metrics (observed_utc);
