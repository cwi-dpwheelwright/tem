# CWI Node Agent Runtime

Decentralized node agent runtime skeleton with config validation, identity keys,
and enrollment CLI.

## Quick start
```bash
cwi-node-agent validate-config --config ./configs/node-agent.example.yaml
cwi-node-agent generate-identity --config ./configs/node-agent.example.yaml
cwi-node-agent enroll --config ./configs/node-agent.example.yaml
cwi-node-agent health --config ./configs/node-agent.example.yaml
cwi-node-agent emit-degradation --config ./configs/node-agent.example.yaml
cwi-node-agent emit-tamper --config ./configs/node-agent.example.yaml
cwi-node-agent unseal-bundle --config ./configs/node-agent.example.yaml
cwi-node-agent collect-link-metrics --config ./configs/node-agent.example.yaml
cwi-node-agent emit-reroute-hint --config ./configs/node-agent.example.yaml
cwi-node-agent emit-status --config ./configs/node-agent.example.yaml
cwi-node-agent run-control-session --config ./configs/node-agent.example.yaml
cwi-node-agent ingest-link-metrics --config ./configs/node-agent.example.yaml --input ./state/metrics/envelope.json
cwi-node-agent serve-health --config ./configs/node-agent.example.yaml --port 8090
cwi-node-agent serve-qr --config ./configs/node-agent.example.yaml --port 8088
cwi-node-agent activate --config ./configs/node-agent.example.yaml --handshake ./state/activation/handshake.json
cwi-node-agent activation-status --config ./configs/node-agent.example.yaml
cwi-node-agent sync-activation-status --config ./configs/node-agent.example.yaml
cwi-node-agent sync-kill-switch --config ./configs/node-agent.example.yaml
cwi-node-agent kill-switch-status --config ./configs/node-agent.example.yaml
cwi-node-agent check-execution --config ./configs/node-agent.example.yaml
cwi-node-agent sync-kill-switch --config ./configs/node-agent.example.yaml --interval 60
```

## Config
Required fields:
- `coordinator_url`: base URL for the coordination layer.
- `enrollment_url`: full URL for enrollment.
- `identity.node_id`: canonical node identifier used in enrollment and all downstream
  node routing. For home nodes, derive this from `/etc/machine-id` using the
  `machine-id-sha256-v1` scheme described below, or set `identity.node_id: auto`
  to let the runtime derive it from `/etc/machine-id` on Linux home nodes
  (legacy `identity.device_id` still accepted).
- `identity.private_key_path` / `identity.public_key_path`: key locations (required when `persist_keys=true`).

Optional fields:
- `identity.key_type`: defaults to `ed25519`.
- `identity.private_key_pem_env` / `identity.public_key_pem_env`: load key PEMs from env vars.
- `identity.persist_keys`: defaults to true; set false to avoid writing keys to disk.
- `storage.state_dir`: directory for durable state (created if missing).
- `enrollment.request_timeout_seconds`: defaults to 10 seconds.
- `enrollment.protocol_version`: defaults to `1.0`.
- `enrollment.cert_fingerprint`: SHA-256 fingerprint (hex) of enrollment client cert.
- `enrollment.mtls.*`: mTLS client cert/key + optional CA bundle/hostname verification.
  - `cert_paths` / `key_paths`: list of cert/key pairs for rotation (exclusive with `cert_path`/`key_path`).
  - `rotation_seconds`: rotate across mTLS certs by time interval.
  - `rotation_grace_seconds`: treat certs expiring within this window as invalid.
  - `revocation_denylist_path`: local denylist of revoked certificate serials.
  - `revocation_crl_url` / `revocation_crl_path`: fetch or load a CRL for revocation checks.
  - `revocation_crl_cache_path`: cache path for fetched CRLs.
  - `revocation_crl_refresh_seconds`: refresh interval for CRL fetch (0 to always fetch).
  - `revocation_crl_timeout_seconds`: timeout for CRL fetch.
  - `revocation_fail_open`: allow enrollment if revocation checks cannot be performed.
- `enrollment.auth.*`: optional enrollment authorization token settings.
  - `token` / `token_env`: supply a bearer token for least-privilege enrollment.
  - `tokens`: list of tokens used for rotation.
  - `token_type`: defaults to `Bearer`.
  - `token_rotation_seconds`: rotates through `tokens` when configured.
- `degradation.*`: thresholds for local load + disk free ratio evaluation.
  - CPU percent + memory free ratio are sampled via `psutil` (installed by default).
  - `transport_url` posts degradation events upstream.
  - `transport_timeout_seconds` controls the upstream post timeout (defaults to 5s).
  - Emits snapshots to `state_dir/degradation/metrics/<event_id>.json` and appends events to `state_dir/degradation/events.jsonl`.
- `readiness.max_degradation_severity`: override readiness threshold (defaults to 0.9).
- `readiness.dependency_urls`: list of dependency health URLs that must return 2xx/3xx.
- `readiness.dependency_timeout_seconds`: timeout per dependency check (defaults to 2s).
- `activation.*`: handshake verification settings (issuer/audience, public key, signature requirement).
- `activation.nonce_cache_max_entries`: maximum nonces stored for replay protection (defaults to 10000).
- `activation.status_url`: override coordinator activation status endpoint.
- `activation.status_timeout_seconds`: timeout for activation status fetch (defaults to 5s).
- `kill_switch.*`: admin URL + break-glass token env for kill-switch propagation and local state path.
  - `break_glass_token_env` is passed as `X-Break-Glass-Token` header when syncing.
  - `retry_attempts` and `retry_backoff_seconds` control retry/backoff for remote fetches.
- `tamper.*`: local tamper indicators + policy hook.
  - `indicator_files_exist` and `indicator_files_sha256` validate critical files.
  - `indicator_env_vars` marks tamper when specific env vars are set.
  - `policy_action` supports `ignore`, `ticket`, `kill`, `kill_irreversible`.
  - `policy_minimum_severity` triggers actions when severity >= threshold.
  - `ticket` writes a local record only (no upstream transport target yet).
- `bundle.*`: encrypted bundle unseal settings.
  - `sealed_bundle_path` points at the encrypted bundle JSON.
  - `unseal_dir` is the output directory for decrypted files.
  - `crypto_provider` supports `noop` and `fernet` (base64 key required for fernet).
  - `crypto_key_path` / `crypto_key_env` supply the decryption key.
- `cache.*`: offline cache storage for packs/procedures.
  - `cache_dir` stores cached assets and index.
  - `max_bytes` / `max_items` enforce bounded eviction.
  - `require_sha256` / `require_signature` enforce integrity checks.
  - `signature_public_key_path` / `signature_public_key_pem_env` supply verification key.
- `baseline_packs.roots`: local pack roots that should always be available as governed
  baseline knowledge during `ask.execute`.
  - These roots are searched before bundle/runtime/cache artifacts.
  - This is the recommended way to place the global core/final packs on home nodes:
    `cwi-intent-interpreter`, `cwi-memory-policy`, `cwi-memory-capsule`,
    `cwi-source-retrieval`, `cwi-identity`, `cwi-values-core`,
    `cwi-values-biblical`, `cwi-ethical-reasoner`, `cwi-safety-guardrails`,
    `cwi-response-orchestrator`, and `cwi-communication-core`.
- `local_model.*`: optional private local inference profile for home nodes.
  - `enabled` advertises local model support to coordination/orchestrator.
  - `provider` currently supports `ollama`.
  - `base_url` / `base_url_env` point the runtime at the local model endpoint.
  - `model` / `model_env`, `model_family`, and `quantization` describe the small
    local profile, for example a 7B/8B instruct model in Q4/Q5.
  - `context_tokens`, `max_vram_gb`, and `timeout_seconds` let the orchestrator
    cap prompt size and route heavier reasoning back to rack/core.
- `resilience.*`: offline queue settings for hostile network conditions.
  - `queue_dir` is the on-disk queue location (defaults to `state_dir/offline_queue`).
  - `max_entries` / `max_bytes` bound persistence.
  - `max_attempts` / `backoff_seconds` control retry behavior.
- `data_boundary.*`: data boundary rules for task payloads.
  - `allow_raw_data` gates raw data handling.
  - `allowed_export_paths` / `allowed_export_hosts` define explicit egress allowlists.
- `update.*`: upgrade/rollback state machine settings.
  - `updates_dir` controls where downloads/staging/state are stored.
  - `current_dir` overrides the runtime current directory (defaults to `updates_dir/current`).
  - `manifest_filename` identifies the manifest in the bundle (defaults to `manifest.json`).
  - `require_manifest` enforces manifest verification for deterministic updates.
  - `require_signature` enforces signed manifests using an Ed25519 public key.
  - `signature_public_key_path` / `signature_public_key_pem_env` supply the verification key.
- `priority.*`: priority routing hints and survival gating.
  - `survival_mode` suppresses non-critical work.
  - `min_priority_level` blocks tasks below the threshold when survival mode is on.
  - `header_name` defines the outbound priority header name.
- `metrics_signing.*`: signed metrics envelope settings.
  - `require_signature` toggles signature enforcement.
  - `private_key_path` / `private_key_pem_env` for signing.
  - `public_key_path` / `public_key_pem_env` for verification.
  - `replay_window` controls accepted sequence window.
- `metrics_ingestion.*`: allowlists for external metrics ingestion.
  - `allowed_node_ids` / `allowed_key_ids` gate accepted envelopes.
  - `allowed_interfaces` / `allowed_interface_types` gate external interface metrics.
- `reroute.*`: reroute hint thresholds + transport.
  - `warning_threshold` / `critical_threshold` / `clear_threshold` control hysteresis.
  - `cooldown_seconds` throttles hint emission.
  - `transport_url` posts reroute hints upstream.
- `job_runner.*`: sandboxed subprocess runner defaults.
  - `allow_commands` gates which commands can execute.
  - `default_timeout_seconds` controls execution timeout.
- `status.*`: heartbeat/status reporting settings.
  - `status_url` posts status payloads upstream.
  - `timeout_seconds` controls post timeout.
- `qr_service.*`: local QR endpoint settings for admin-assisted home-node linking.
  - `identifier_version` defaults to `machine-id-sha256-v1`.
  - `allowed_networks` restricts which client CIDRs can fetch the QR/payload pages.
  - The emitted QR payload contains the immutable `node_id` and `identifier_version`; tenant is resolved during linking from the signed-in session and registered node membership.
- `control_session.*`: persistent home-node control channel settings.
  - `connect_url` overrides the derived WebSocket URL (otherwise `coordinator_url` becomes `/control/nodes/connect`).
  - `tenant_id` / `owner_user_id` bind the node hello and token validation path.
  - `tenant_id_env` / `owner_user_id_env` can source those bindings from environment for per-user QR/link provisioning without baking a user id into the YAML.
  - `protocol_version`, `keepalive_interval_seconds`, `heartbeat_interval_seconds`, and `reconnect_backoff_seconds` govern the session lifecycle.
  - `work_token_issuer` and `work_token_secret` / `work_token_secret_env` validate dispatched work tokens.
  - `replay_cache_max_entries` bounds replay protection for work ids.
- `link_metrics.*`: link metrics transport settings.
  - `transport_url` posts link metrics reports upstream.
  - `transport_timeout_seconds` controls request timeout.
  - `use_envelope` wraps reports in signed envelopes when enabled.

## Local Process Miner
When `storage.state_dir` is configured, the runtime exposes
`process_miner.observe` as a local, observe-only task. It records sanitized
process observations under `storage.state_dir/process_miner` and can produce a
privacy-safe federated-learning summary when `emit_summary` is requested and
the payload policy status is approved.

The Process Miner does not mutate memory, graph, or vector state. Raw text,
messages, transcripts, file contents, emails, notes, and similar private fields
are stripped before local observation records or summaries are written. Summary
payloads are local artifacts only; upstream submission remains controlled by the
orchestrator's federated-learning policy and transport configuration.

## Home node identity and QR linking
For customer-delivered home nodes, `identity.node_id` should be treated as the
immutable identifier that a user links to from the GPT site.

Recommended derivation:
1. Read `/etc/machine-id`.
2. Normalize to lowercase hex with no whitespace.
3. Compute `sha256(machine-id)`.
4. Truncate to the first 32 hex chars.
5. Prefix with `home-`.

If you want the runtime to do this derivation itself, set `identity.node_id`
to `auto`. You can optionally override the source file with
`identity.machine_id_path`.

Example:
`home-032c311b95ac9a14a81f0b3fa2520ae9`

Recommended QR payload:

```json
{
  "node_id": "home-032c311b95ac9a14a81f0b3fa2520ae9",
  "identifier_version": "machine-id-sha256-v1"
}
```

Use immutable identifiers in the QR payload. Do not use hostname, Tailscale IP,
or certificate fingerprint as the primary user-link key.

## QR service
`serve-qr` runs a lightweight HTTP service for administrators on the VPN. It
serves:
- `/`: HTML page with the QR code and payload preview.
- `/qr.svg`: SVG QR code containing the JSON payload.
- `/payload.json`: raw JSON payload used by the GPT-site scanner.
- `/health`: simple health response.

The service denies clients outside the configured `qr_service.allowed_networks`.
For container deployments on Linux, the example compose uses `network_mode: host`
so the QR service listens directly on the node's VPN-facing interfaces.
If you configure `identity.node_id: auto`, also bind-mount the host
`/etc/machine-id` into the container.

For the end-to-end customer onboarding flow, see
`OPS/cwi-ops/runbooks/home-node-linking.md`.

## Enrollment flow (skeleton)
The enrollment CLI generates (or loads) an Ed25519 keypair, builds a JSON
request aligned to the coordination-layer membership API, and POSTs it to
`/api/membership/enroll`. If `enrollment.mtls.cert_path` is provided, the CLI
uses the cert for mTLS and computes its SHA-256 fingerprint for the payload.

## Activation flow (skeleton)
Use `cwi-node-agent activate` to verify an activation handshake artifact,
enforce nonce replay protection, and persist activation state in
`state_dir/activation/state.json`. Nonces are cached in
`state_dir/activation/nonce_cache.json` with a configurable max entry cap.
`sync-activation-status` fetches activation status from the coordinator and
persists the result to `state_dir/activation/remote_status.json`, clearing local
activation state when remote status is not active.

## Kill-switch propagation (skeleton)
Use `cwi-node-agent sync-kill-switch` to fetch the kill-switch status from
the orchestrator admin endpoint and persist it locally in
`state_dir/kill_switch/state.json`. `check-execution` enforces activation
validity and kill-switch state before allowing task execution.
`sync-kill-switch --interval <seconds>` runs a periodic refresh loop.
If an irreversible kill-switch is observed, the local state remains enabled
even if the remote endpoint later clears it.

## Tamper indicators (skeleton)
Use `cwi-node-agent emit-tamper` to evaluate tamper indicators and emit a
structured tamper event to `state_dir/tamper/events.jsonl`. If the configured
`policy_action` is `ticket` or `kill`, the event processing will write a ticket
record or persist a local kill-switch state. Tickets are local-only until an
upstream transport target is defined.

## Encrypted bundle unseal (skeleton)
Use `cwi-node-agent unseal-bundle` to decrypt a bundle and unpack files into
the configured output directory. The bundle payload is a JSON object that
contains a base64-encoded encrypted payload; decrypted payloads must include a
`files` map of relative paths to base64 file content.

## Offline cache (skeleton)
`node_agent_runtime.cache` provides a bounded cache for offline procedures/packs.
Assets are stored with sha256 (and optional signatures) and evicted by age when
size or item limits are exceeded.

## Baseline packs
Home nodes can carry governed baseline packs locally by listing their unpacked
roots under `baseline_packs.roots`. These roots participate in `local_ask`
retrieval and are intended for shared packs such as:
- `cwi-intent-interpreter`
- `cwi-memory-policy`
- `cwi-memory-capsule`
- `cwi-source-retrieval`
- `cwi-identity`
- `cwi-values-core`
- `cwi-values-biblical`
- `cwi-ethical-reasoner`
- `cwi-safety-guardrails`
- `cwi-response-orchestrator`
- `cwi-communication-core`

For system-truth queries, local ask gives these governed roots priority so
identity answers stay aligned with the global communication baseline even when
the node is working from local artifacts.

## Offline queue (skeleton)
Outbound events can be queued when transports fail. Degradation events use the
queue if configured, and retries are attempted with exponential backoff. When
queue limits are exceeded, the oldest entries are dropped.

## Data boundary (skeleton)
Task payloads can be gated on data classification and explicit export allowlists.
Raw data handling is blocked by default unless explicitly enabled.

## Upgrade + rollback (skeleton)
Upgrade workflow downloads a sealed bundle, stages it, swaps to the new runtime,
and rolls back to the previous version if the health check fails. The last-known-
good runtime is preserved in `updates_dir/previous`.

## Priority routing hints (skeleton)
Tasks can include priority metadata (`priority` or `priority_level`). In survival
mode, tasks below `min_priority_level` are suppressed; priority headers/metadata
can be generated for outbound client calls.

## Metrics signing + replay (skeleton)
Metrics payloads can be wrapped in a signed envelope with a monotonic sequence.
Replay windows are enforced when verifying envelopes.

## Link metrics (skeleton)
`collect-link-metrics` gathers normalized interface metrics via adapters and
emits a schema-aligned report with radio-quality fields (latency/jitter/loss/
throughput/RSSI/SNR). The default adapter uses OS counters for throughput and
labels interface types by name. When configured, reports are posted to the
coordination-layer transport endpoint using signed envelopes.

## Reroute hints (skeleton)
Reroute hints are emitted when degradation severity crosses thresholds and
cleared after recovery. Hysteresis and cooldown prevent flapping.

## Job runner (skeleton)
`job_runner` runs subprocess tasks with an allowlist and timeout for sandboxing.

## Status reporting (skeleton)
`emit-status` writes status records to `state_dir/status/reports.jsonl` and can
optionally post to a configured status URL.

## Task runtime (skeleton)
`node_agent_runtime.task_runtime.TaskRuntime` provides a registry-based task
runner that enforces activation + kill-switch gating before delegating to
handlers. This is the intended integration point for future task execution.

## Control session (v1)
`run-control-session` opens the persistent outbound WebSocket control channel,
sends `node_hello`, emits keepalives/heartbeats, validates signed work tokens,
executes `ask.execute` through the task runtime, and returns `work_ack` plus
`work_result` messages over the same session. The built-in `ask.execute`
handler searches governed baseline packs, local unsealed bundle content, and
cached artifacts for matching evidence. When the orchestrator routes a request
with `execution.route_mode: local_model` and `local_model.enabled` is true, the
handler asks the configured local Ollama model using compact governance and
relevant evidence chunks. Approved local subprocess work still flows through
`job_runner`.

## Testing
Integration test env vars:
- `CWI_KILL_SWITCH_ADMIN_URL`
- `CWI_BREAK_GLASS_TOKEN`
- `CWI_BREAK_GLASS_ISSUE_URL` (optional if `CWI_BREAK_GLASS_TOKEN` is set)
- `CWI_BREAK_GLASS_ROLE`
- `CWI_BREAK_GLASS_SCOPE`
- `CWI_BREAK_GLASS_USER_ID`
- `CWI_BREAK_GLASS_DURATION_MINUTES` (optional, defaults to 5)
- `CWI_BREAK_GLASS_REASON` (optional)
- `CWI_TENANT_ID`

## Packaging
```bash
pip install -e .
```
