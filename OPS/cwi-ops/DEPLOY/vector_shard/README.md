# DEPLOY: vector_shard

## Purpose
Compose bundle for the vector shard role (Qdrant).

## Files
- docker-compose.yml
- .env.template (copy to .env)

## Setup
1. Copy .env.template to .env and update values.
2. Validate env:
   python OPS/cwi-ops/scripts/validate-env.py --role vector_shard --env-file OPS/cwi-ops/DEPLOY/vector_shard/.env
   .\OPS\cwi-ops\scripts\validate-env.ps1 -Role vector_shard -EnvFile OPS/cwi-ops/DEPLOY/vector_shard/.env
3. Start service:
   docker compose up -d

## Smoke checks
PowerShell:
```
.\OPS\cwi-ops\scripts\smoke-vector-shard.ps1 -BaseUrl http://localhost:6333 -RequireCollection
```

## Network access (firewall)
The compose bundle only exposes Qdrant on the internal `cwi-internal` network and does not publish host ports.
Keep it that way in shared environments so only internal services (orchestrator, ETL, connectors) can reach it.

If you need local access for debugging, temporarily publish the HTTP port in a local override file and remove it
afterwards.

## Security hardening
Use the internal `cwi-internal` network only. Do not publish 6333/6334 to the host in shared environments.

### API key auth + rotation (optional)
Qdrant supports API key auth via `service.api_key` (and optional `service.read_only_api_key`).
When enabled, rotate keys by generating a new key, updating the container environment, and restarting the service.

Example compose override snippet:
```
services:
  cwi-vector-shard:
    environment:
      QDRANT__SERVICE__API_KEY: "<new-key>"
      QDRANT__SERVICE__READ_ONLY_API_KEY: "<new-readonly-key>"
```

Rotation steps:
1. Generate a new API key (and read-only key if used) in your secret manager.
2. Update the compose override/env file with the new values.
3. Restart `cwi-vector-shard` to apply the new keys.
4. Revoke the old keys in the secret manager.

### Verify firewall posture
Confirm that no host ports are published:

```
docker compose ps
```

You should see `cwi-vector-shard` without a host port mapping for 6333/6334.

Optional host check (should fail when no ports are published):

PowerShell:
```
Test-NetConnection -ComputerName localhost -Port 6333
```

bash:
```
nc -vz localhost 6333
```

## Collection bootstrap
Define the collection name and embedding dimension in `.env`:

- VECTOR_COLLECTION (default recommendation: `cwi_corpus`)
- VECTOR_EMBEDDING_DIMENSION (must match the orchestrator embedding size)

Create the collection from inside the `cwi-internal` network:

PowerShell:
```
docker run --rm --network cwi-internal curlimages/curl ^
  -X PUT "http://cwi-vector-shard:6333/collections/$env:VECTOR_COLLECTION" ^
  -H "Content-Type: application/json" ^
  -d "{\"vectors\":{\"size\":$env:VECTOR_EMBEDDING_DIMENSION,\"distance\":\"Cosine\"}}"
```

bash:
```
docker run --rm --network cwi-internal curlimages/curl \
  -X PUT "http://cwi-vector-shard:6333/collections/${VECTOR_COLLECTION}" \
  -H "Content-Type: application/json" \
  -d "{\"vectors\":{\"size\":${VECTOR_EMBEDDING_DIMENSION},\"distance\":\"Cosine\"}}"
```

To verify the collection exists:
```
docker run --rm --network cwi-internal curlimages/curl \
  "http://cwi-vector-shard:6333/collections/${VECTOR_COLLECTION}"
```

## Payload schema for retention + tenancy
Evidence vectors must include:
- `tenant_id` (string)
- `created_at` (epoch seconds UTC)
- `pack_id` (string, when the evidence is scoped to a governed pack)
- `pack_hash` or `pack_version` (string, used for active-pack filtering)

These fields enable tenant-scoped queries and retention purges. Example filter (tenant + cutoff):
```
{
  "filter": {
    "must": [
      { "key": "tenant_id", "match": { "value": "tenant-1" } },
      { "key": "created_at", "range": { "lte": 1735689600 } }
    ]
  }
}
```

When retrieval should align to the active governed pack, include `pack_id` plus the
resolved `pack_hash` (preferred) or `pack_version` in the query filter.

## Retention purge (scheduled)
Use the purge helper to delete expired vectors. Always take an encrypted backup first.

Dry-run count:
```
python OPS/cwi-ops/scripts/purge-vector-shard.py \
  --base-url http://cwi-vector-shard:6333 \
  --collection ${VECTOR_COLLECTION} \
  --retention-days 90 \
  --dry-run
```

Delete with backup marker enforcement:
```
python OPS/cwi-ops/scripts/purge-vector-shard.py \
  --base-url http://cwi-vector-shard:6333 \
  --collection ${VECTOR_COLLECTION} \
  --retention-days 90 \
  --backup-marker /opt/cwi/backups/qdrant-backup.marker
```

Example cron (daily at 02:15):
```
15 2 * * * python /opt/cwi/OPS/cwi-ops/scripts/purge-vector-shard.py --base-url http://cwi-vector-shard:6333 --collection ${VECTOR_COLLECTION} --retention-days 90 --backup-marker /opt/cwi/backups/qdrant-backup.marker
```

Example Windows Task Scheduler action:
```
py C:\opt\cwi\OPS\cwi-ops\scripts\purge-vector-shard.py --base-url http://cwi-vector-shard:6333 --collection %VECTOR_COLLECTION% --retention-days 90 --backup-marker C:\opt\cwi\backups\qdrant-backup.marker
```

## Backup and restore (filesystem snapshot)
This procedure snapshots the mounted storage directory. Stop the service first to avoid an inconsistent backup.

### Encrypted backups (required in shared environments)
Store only encrypted backups (no plaintext snapshots). Use a streaming approach so unencrypted archives are never written
to disk. Examples:

bash (age):
```
timestamp=$(date +%Y%m%d-%H%M%S)
tar -czf - -C "${VECTOR_SHARD_DATA_DIR}" . | age -r "<age-public-key>" \
  > "$(dirname "${VECTOR_SHARD_DATA_DIR}")/qdrant-backup-${timestamp}.tar.gz.age"
```

bash (gpg symmetric):
```
timestamp=$(date +%Y%m%d-%H%M%S)
tar -czf - -C "${VECTOR_SHARD_DATA_DIR}" . | gpg --symmetric --cipher-algo AES256 \
  -o "$(dirname "${VECTOR_SHARD_DATA_DIR}")/qdrant-backup-${timestamp}.tar.gz.gpg"
```

PowerShell (7zip, creates encrypted archive directly):
```
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
7z a -t7z -mhe=on -p"$env:BACKUP_PASSPHRASE" `
  "$env:VECTOR_SHARD_DATA_DIR\\..\\qdrant-backup-$timestamp.7z" `
  "$env:VECTOR_SHARD_DATA_DIR\\*"
```

Verify no plaintext snapshots exist:
```
Get-ChildItem "$env:VECTOR_SHARD_DATA_DIR\\.." -Filter "qdrant-backup-*.tar.gz"
```

### Backup
1. Stop the shard:
   ```
   docker compose stop cwi-vector-shard
   ```
2. Archive the storage directory on the host (path = `VECTOR_SHARD_DATA_DIR`):

PowerShell:
```
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
Compress-Archive -Path "$env:VECTOR_SHARD_DATA_DIR\\*" -DestinationPath "$env:VECTOR_SHARD_DATA_DIR\\..\\qdrant-backup-$timestamp.zip"
```

bash:
```
timestamp=$(date +%Y%m%d-%H%M%S)
tar -czf "$(dirname "${VECTOR_SHARD_DATA_DIR}")/qdrant-backup-${timestamp}.tar.gz" -C "${VECTOR_SHARD_DATA_DIR}" .
```
3. Restart the shard:
   ```
   docker compose start cwi-vector-shard
   ```

### Restore + verify
1. Stop the shard:
   ```
   docker compose stop cwi-vector-shard
   ```
2. Replace the storage directory contents with the backup archive you created.
3. Restart the shard:
   ```
   docker compose start cwi-vector-shard
   ```
4. Verify the collection is present:
   ```
   docker run --rm --network cwi-internal curlimages/curl \
    "http://cwi-vector-shard:6333/collections/${VECTOR_COLLECTION}"
```

## Observability
Prometheus scraping is defined in `OPS/cwi-ops/observability/prometheus.yml` with `metrics_path: /metrics`.
Import the Grafana dashboard at `OPS/cwi-ops/observability/grafana/qdrant-dashboard.json` and point it at the
Prometheus datasource.

Alert rules live in `OPS/cwi-ops/observability/prometheus-alerts.yml`:
- `CwiVectorShardDown`: fires after 2 minutes with `up == 0`.
- `CwiVectorShardStoragePressure`: fires after 10 minutes below 15% free space on `/qdrant/storage` (requires node_exporter).
