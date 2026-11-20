param(
  [ValidateSet("enable", "disable")]
  [string]$Mode = "enable",
  [string]$TargetHost = "100.120.208.51",
  [string]$User = "root",
  [string]$VectorShard1 = "http://100.115.118.16:6333",
  [string]$VectorShard2 = "http://100.91.142.103:6333",
  [string]$ModelUrl = "http://100.124.140.66:11434"
)

$ErrorActionPreference = "Stop"
$remote = "$User@$TargetHost"

function Invoke-Remote([string]$Command) {
  & ssh $remote $Command
}

if ($Mode -eq "enable") {
  & scp "OPS/cwi-ops/scripts/vector-shard-stub.py" "${remote}:/opt/cwi/DEPLOY/mcp-core/vector-shard-stub.py"
  & scp "OPS/cwi-ops/scripts/model-stub.py" "${remote}:/opt/cwi/DEPLOY/mcp-core/model-stub.py"

  Invoke-Remote "docker image inspect local/cwi-teacher-node:10h >/dev/null 2>&1"
  Invoke-Remote "docker rm -f internal-02-data-vector-stub internal-02-model-stub >/dev/null 2>&1 || true"
  Invoke-Remote "docker run -d --name internal-02-data-vector-stub --network cwi-internal -v /opt/cwi/DEPLOY/mcp-core/vector-shard-stub.py:/app/vector-shard-stub.py:ro local/cwi-teacher-node:10h python3 /app/vector-shard-stub.py"
  Invoke-Remote "docker run -d --name internal-02-model-stub --network cwi-internal -v /opt/cwi/DEPLOY/mcp-core/model-stub.py:/app/model-stub.py:ro local/cwi-teacher-node:10h python3 /app/model-stub.py"

  $envCmd = "cd /opt/cwi/DEPLOY/mcp-core" +
    " && sed -i 's#^VECTOR_SHARD_1_URL=.*#VECTOR_SHARD_1_URL=http://internal-02-data-vector-stub:6333#' .env" +
    " && sed -i 's#^VECTOR_SHARD_2_URL=.*#VECTOR_SHARD_2_URL=http://internal-02-data-vector-stub:6333#' .env" +
    " && sed -i 's#^MODEL_PRO_URL=.*#MODEL_PRO_URL=http://internal-02-model-stub:11434#' .env" +
    " && sed -i 's#^MODEL_FREE_URL=.*#MODEL_FREE_URL=http://internal-02-model-stub:11434#' .env" +
    " && sed -i 's#^MODEL_BASE_URL=.*#MODEL_BASE_URL=http://internal-02-model-stub:11434#' .env" +
    " && docker compose --env-file .env -f docker-compose.yml up -d cwi-orchestrator cwi-router"
  Invoke-Remote $envCmd
}
else {
  Invoke-Remote "docker rm -f internal-02-data-vector-stub internal-02-model-stub >/dev/null 2>&1 || true"

  $envCmd = "cd /opt/cwi/DEPLOY/mcp-core" +
    " && sed -i 's#^VECTOR_SHARD_1_URL=.*#VECTOR_SHARD_1_URL=$VectorShard1#' .env" +
    " && sed -i 's#^VECTOR_SHARD_2_URL=.*#VECTOR_SHARD_2_URL=$VectorShard2#' .env" +
    " && sed -i 's#^MODEL_PRO_URL=.*#MODEL_PRO_URL=$ModelUrl#' .env" +
    " && sed -i 's#^MODEL_FREE_URL=.*#MODEL_FREE_URL=$ModelUrl#' .env" +
    " && sed -i 's#^MODEL_BASE_URL=.*#MODEL_BASE_URL=$ModelUrl#' .env" +
    " && docker compose --env-file .env -f docker-compose.yml up -d cwi-orchestrator cwi-router"
  Invoke-Remote $envCmd
}

Write-Host "Done: $Mode"
