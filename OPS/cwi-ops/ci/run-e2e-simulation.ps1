param(
  [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
  [string]$EnvFile = "OPS/cwi-ops/compose/test/.env",
  [string]$Profile = "simulation",
  [string]$BaseUrl = "http://localhost:18081",
  [string]$OutputDir = "artifacts/qa/e2e-simulation",
  [string]$MtlsRotationCmd = "",
  [string]$MtlsHealthUrl = "",
  [int]$MtlsTimeoutSeconds = 120,
  [double]$MtlsPollSeconds = 5,
  [switch]$SkipCleanup,
  [switch]$SkipBuildImages,
  [switch]$KeepRunning
)

$ErrorActionPreference = "Stop"

function Write-Section {
  param([string]$Message)
  Write-Host ""
  Write-Host "== $Message =="
}

function Resolve-EnvPath {
  param(
    [string]$Value,
    [string]$BaseDir
  )
  if (-not $Value) {
    return $Value
  }
  if ([IO.Path]::IsPathRooted($Value)) {
    return $Value
  }
  return (Join-Path $BaseDir $Value)
}

function Import-EnvFile {
  param([string]$Path)

  if (-not (Test-Path $Path)) {
    throw "Env file not found: $Path"
  }

  $baseDir = [IO.Path]::GetFullPath((Split-Path $Path -Parent))

  Get-Content -LiteralPath $Path | ForEach-Object {
    $line = $_.Trim()
    if (-not $line -or $line.StartsWith("#")) {
      return
    }
    if ($line -notmatch "=") {
      return
    }
    $parts = $line -split "=", 2
    $name = $parts[0].Trim()
    $value = $parts[1]
    if ($value.StartsWith('"') -and $value.EndsWith('"')) {
      $value = $value.Substring(1, $value.Length - 2)
    }

    if ($name -in @(
      "PACKS_DIR",
      "VECTOR_SHARD_DATA_DIR",
      "CWI_GRAPH_DB_DATA_DIR",
      "CWI_MODEL_WORKER_LIFECYCLE_DIR"
    )) {
      $value = Resolve-EnvPath -Value $value -BaseDir $baseDir
    }

    if ($name) {
      Set-Item -Path "Env:$name" -Value $value
    }
  }
}

function Assert-Python {
  $versionText = & python --version 2>$null
  if (-not $versionText) {
    throw "Python is required to run gold tests."
  }
  if ($versionText -match "Python\s+(\d+)\.(\d+)") {
    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    if ($major -lt 3 -or ($major -eq 3 -and $minor -lt 10)) {
      throw "Python 3.10+ is required. Found: $versionText"
    }
  }
}

function Ensure-AuthToken {
  param([string]$IdentityBaseUrl)

  if ($env:CWI_AUTH_TOKEN) {
    return
  }

  $tenantId = $env:IDENTITY_BOOTSTRAP_TENANT_ID
  if (-not $tenantId) {
    $tenantId = "tenant-1"
  }

  $password = $env:IDENTITY_BOOTSTRAP_ADMIN_PASSWORD
  if (-not $password) {
    throw "IDENTITY_BOOTSTRAP_ADMIN_PASSWORD is required to fetch CWI_AUTH_TOKEN."
  }

  $payload = @{
    tenant_id = $tenantId
    password  = $password
  }

  if ($env:IDENTITY_BOOTSTRAP_ADMIN_EMAIL) {
    $payload["email"] = $env:IDENTITY_BOOTSTRAP_ADMIN_EMAIL
  } elseif ($env:IDENTITY_BOOTSTRAP_ADMIN_USERNAME) {
    $payload["username"] = $env:IDENTITY_BOOTSTRAP_ADMIN_USERNAME
  } else {
    throw "IDENTITY_BOOTSTRAP_ADMIN_EMAIL or IDENTITY_BOOTSTRAP_ADMIN_USERNAME is required to fetch CWI_AUTH_TOKEN."
  }

  $resp = Invoke-RestMethod -Method Post -Uri "$IdentityBaseUrl/auth/login" -ContentType "application/json" -Body ($payload | ConvertTo-Json)
  if (-not $resp -or -not $resp.access_token) {
    throw "Failed to fetch CWI_AUTH_TOKEN from identity."
  }

  $env:CWI_AUTH_TOKEN = $resp.access_token
}

function Invoke-Logged {
  param(
    [scriptblock]$Action,
    [string]$LogPath
  )
  & $Action 2>&1 | Tee-Object -FilePath $LogPath -Append
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed. See log: $LogPath"
  }
}

function Assert-LastExit {
  param([string]$Context)
  if ($LASTEXITCODE -ne 0) {
    throw "$Context failed with exit code $LASTEXITCODE"
  }
}

function Invoke-With-Retry {
  param(
    [scriptblock]$Action,
    [int]$MaxAttempts = 20,
    [int]$DelaySeconds = 3
  )

  for ($attempt = 1; $attempt -le $MaxAttempts; $attempt++) {
    try {
      & $Action
      return
    } catch {
      if ($attempt -ge $MaxAttempts) {
        throw
      }
      Write-Host "Health check attempt $attempt failed: $($_.Exception.Message)"
      Start-Sleep -Seconds $DelaySeconds
    }
  }
}

function Invoke-GoldSuite {
  param(
    [string]$Suite,
    [string]$RunDir,
    [string]$BaseUrl
  )
  $logPath = Join-Path $RunDir "gold-$Suite.log"
  $junitPath = Join-Path $RunDir "gold-$Suite.junit.xml"
  $summaryPath = Join-Path $RunDir "gold-$Suite.summary.json"

  Invoke-Logged -LogPath $logPath -Action {
    python tools/cwi-gold-tests/run_tests.py --suite $Suite --base-url $BaseUrl --junit-xml $junitPath --json-summary $summaryPath
  }
}

function Invoke-PytestSubset {
  param(
    [string]$TestPath,
    [string]$Filter,
    [string]$RunDir,
    [string]$Label
  )
  $logPath = Join-Path $RunDir "$Label.log"
  $junitPath = Join-Path $RunDir "$Label.junit.xml"
  Invoke-Logged -LogPath $logPath -Action {
    python -m pytest -m simulation $TestPath -k $Filter --junitxml $junitPath
  }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$timestamp = Get-Date -Format "yyyyMMdd-HHmmss"
$runDir = Join-Path $repoRoot (Join-Path $OutputDir $timestamp)
New-Item -ItemType Directory -Force -Path $runDir | Out-Null

Push-Location $repoRoot
try {
  Write-Section "Load environment"
  Import-EnvFile -Path $EnvFile

  $env:CWI_COMPOSE_FILE = [IO.Path]::GetFullPath($ComposeFile)
  $env:CWI_COMPOSE_ENV_FILE = [IO.Path]::GetFullPath($EnvFile)

  $identityHostPort = if ([string]::IsNullOrWhiteSpace($env:IDENTITY_HOST_PORT)) { $env:IDENTITY_HTTP_PORT } else { $env:IDENTITY_HOST_PORT }


  if (-not $env:REGISTRY) { $env:REGISTRY = "local" }
  if (-not $env:CWI_ROUTER_TAG) { $env:CWI_ROUTER_TAG = "10h" }
  if (-not $env:CWI_ORCHESTRATOR_TAG) { $env:CWI_ORCHESTRATOR_TAG = "10h" }
  if (-not $env:CWI_IDENTITY_TAG) { $env:CWI_IDENTITY_TAG = "10h" }
  if (-not $env:CWI_COORDINATION_TAG) { $env:CWI_COORDINATION_TAG = "10h" }
  if (-not $env:IDENTITY_CORS_ORIGINS) { $env:IDENTITY_CORS_ORIGINS = $BaseUrl }

  $composeArgs = @("-f", $ComposeFile, "--env-file", $EnvFile, "--profile", $Profile)

  if (-not $SkipCleanup) {
    Write-Section "Cleanup prior state"
    & docker compose @composeArgs down --rmi local --volumes --remove-orphans | Tee-Object -FilePath (Join-Path $runDir "compose-down.log")
    Assert-LastExit "docker compose down"

    foreach ($image in @(
      "$($env:REGISTRY)/cwi-router:$($env:CWI_ROUTER_TAG)",
      "$($env:REGISTRY)/cwi-orchestrator:$($env:CWI_ORCHESTRATOR_TAG)",
      "$($env:REGISTRY)/cwi-identity-server:$($env:CWI_IDENTITY_TAG)"
    )) {
      try {
        & docker image rm -f $image | Out-Null
      } catch {
      }
    }
  }

  if (-not $SkipBuildImages) {
    Write-Section "Build core images"
    & OPS/cwi-ops/ci/build-images.ps1 -Registry $env:REGISTRY -RouterTag $env:CWI_ROUTER_TAG -OrchestratorTag $env:CWI_ORCHESTRATOR_TAG -IdentityTag $env:CWI_IDENTITY_TAG `
      | Tee-Object -FilePath (Join-Path $runDir "build-images.log")
    Assert-LastExit "build images"
    & docker build -f src/backend/services/cwi-coordination-layer/Dockerfile -t "$($env:REGISTRY)/cwi-coordination-layer:$($env:CWI_COORDINATION_TAG)" . | Tee-Object -FilePath (Join-Path $runDir "build-coordination-layer.log")
    Assert-LastExit "build coordination layer"
  }

  Write-Section "Start compose stack"
  & docker compose @composeArgs up -d | Tee-Object -FilePath (Join-Path $runDir "compose-up.log")
  Assert-LastExit "docker compose up"

  Write-Section "Wait for health"
  $healthLog = Join-Path $runDir "full-stack-smoke.log"
  Invoke-With-Retry -MaxAttempts 20 -DelaySeconds 3 -Action {
    & OPS/cwi-ops/scripts/full-stack-smoke.ps1 -GatewayBaseUrl $BaseUrl -IdentityBaseUrl "http://localhost:$identityHostPort" -PackServiceBaseUrl "http://localhost:$($env:PACK_SERVICE_HTTP_PORT)" -SkipTeamsBot -SkipNodeUi | Tee-Object -FilePath $healthLog -Append
    Assert-LastExit "full stack smoke"
  }

  Write-Section "Prepare test environment"
  Assert-Python
  python -m pip install -q pytest requests schemathesis httpx jsonschema pyyaml defusedxml

  $env:CWI_BASE_URL = $BaseUrl
  if (-not $env:CWI_COORDINATION_URL -and $env:COORDINATION_HOST_PORT) {
    $env:CWI_COORDINATION_URL = "http://localhost:$($env:COORDINATION_HOST_PORT)"
  }
  if (-not $env:CWI_ROUTER_BASE_URL -and $env:ROUTER_HOST_PORT) {
    $env:CWI_ROUTER_BASE_URL = "http://localhost:$($env:ROUTER_HOST_PORT)"
  }

  $env:CWI_READY_URL = "$BaseUrl/api/ready"
  $env:CWI_PACK_NAME = "enterprise"
  $env:CWI_TENANT_ID = "tenant-1"

  $env:CWI_E2E_BACKEND_FLOWS = "1"
  $env:CWI_E2E_BACKEND_LEDGER = "1"

  $env:CWI_EXPECT_ROUTER_DOWN = "1"
  $env:CWI_EXPECT_READINESS_GATES = "1"
  $env:CWI_EXPECT_PACK_CORRUPT = "1"
  $env:CWI_CORRUPT_PACK_NAME = "corrupt"

  if ($MtlsRotationCmd) { $env:CWI_MTLS_ROTATION_CMD = $MtlsRotationCmd }
  if ($MtlsHealthUrl) { $env:CWI_MTLS_HEALTH_URL = $MtlsHealthUrl }
  $env:CWI_MTLS_ROTATION_TIMEOUT_SECONDS = "$MtlsTimeoutSeconds"
  $env:CWI_MTLS_ROTATION_POLL_SECONDS = "$MtlsPollSeconds"

  Ensure-AuthToken -IdentityBaseUrl "http://localhost:$identityHostPort"

  Write-Section "Run gold simulation suite"
  Invoke-GoldSuite -Suite "simulation" -RunDir $runDir -BaseUrl $BaseUrl

  Write-Section "Run gold e2e suite"
  Invoke-GoldSuite -Suite "e2e" -RunDir $runDir -BaseUrl $BaseUrl

  Write-Section "Simulation: pack service down"
  & docker compose @composeArgs stop cwi-pack-service | Tee-Object -FilePath (Join-Path $runDir "pack-service-down.log")
  Assert-LastExit "stop pack service"
  try {
    $env:CWI_EXPECT_PACK_SERVICE_DOWN = "1"
    Invoke-PytestSubset -TestPath "tools/cwi-gold-tests/tests/test_simulation.py" -Filter "pack_service_down" -RunDir $runDir -Label "simulation-pack-service-down"
  } finally {
    $env:CWI_EXPECT_PACK_SERVICE_DOWN = ""
    & docker compose @composeArgs start cwi-pack-service | Tee-Object -FilePath (Join-Path $runDir "pack-service-up.log")
    Assert-LastExit "start pack service"
  }

  Write-Section "Simulation: redis down"
  & docker compose @composeArgs stop redis | Tee-Object -FilePath (Join-Path $runDir "redis-down.log")
  Assert-LastExit "stop redis"
  try {
    $env:CWI_EXPECT_REDIS_DOWN = "1"
    Invoke-PytestSubset -TestPath "tools/cwi-gold-tests/tests/test_simulation.py" -Filter "redis_down" -RunDir $runDir -Label "simulation-redis-down"
  } finally {
    $env:CWI_EXPECT_REDIS_DOWN = ""
    & docker compose @composeArgs start redis | Tee-Object -FilePath (Join-Path $runDir "redis-up.log")
    Assert-LastExit "start redis"
  }

  Write-Section "Simulation: postgres down"
  & docker compose @composeArgs stop postgres | Tee-Object -FilePath (Join-Path $runDir "postgres-down.log")
  Assert-LastExit "stop postgres"
  try {
    $env:CWI_EXPECT_DB_DOWN = "1"
    Invoke-PytestSubset -TestPath "tools/cwi-gold-tests/tests/test_simulation.py" -Filter "postgres_down" -RunDir $runDir -Label "simulation-postgres-down"
  } finally {
    $env:CWI_EXPECT_DB_DOWN = ""
    & docker compose @composeArgs start postgres | Tee-Object -FilePath (Join-Path $runDir "postgres-up.log")
    Assert-LastExit "start postgres"
  }

  Write-Section "Simulation: vector shard down"
  & docker compose @composeArgs stop cwi-vector-shard | Tee-Object -FilePath (Join-Path $runDir "vector-shard-down.log")
  Assert-LastExit "stop vector shard"
  Start-Sleep -Seconds 5
  try {
    $env:CWI_EXPECT_VECTOR_SHARD1_DOWN = "1"
    $env:CWI_EXPECT_VECTOR_SHARD2_DOWN = "1"
    $env:CWI_EXPECT_VECTOR_SHARDS_DOWN = "1"
    Invoke-PytestSubset -TestPath "tools/cwi-gold-tests/tests/test_simulation.py" -Filter "vector_shard" -RunDir $runDir -Label "simulation-vector-shard-down"
  } finally {
    $env:CWI_EXPECT_VECTOR_SHARD1_DOWN = ""
    $env:CWI_EXPECT_VECTOR_SHARD2_DOWN = ""
    $env:CWI_EXPECT_VECTOR_SHARDS_DOWN = ""
    & docker compose @composeArgs start cwi-vector-shard | Tee-Object -FilePath (Join-Path $runDir "vector-shard-up.log")
    Assert-LastExit "start vector shard"
  }

  Write-Section "Simulation: ledger down"
  & docker compose @composeArgs stop cwi-graph-db | Tee-Object -FilePath (Join-Path $runDir "graph-db-down.log")
  Assert-LastExit "stop ledger"
  Start-Sleep -Seconds 5
  try {
    $env:CWI_EXPECT_LEDGER_DOWN = "1"
    Invoke-PytestSubset -TestPath "tools/cwi-gold-tests/tests/test_simulation.py" -Filter "ledger_down" -RunDir $runDir -Label "simulation-ledger-down"
  } finally {
    $env:CWI_EXPECT_LEDGER_DOWN = ""
    & docker compose @composeArgs start cwi-graph-db | Tee-Object -FilePath (Join-Path $runDir "graph-db-up.log")
    Assert-LastExit "start ledger"
  }

  Write-Section "Simulation: model pro down"
  & docker compose @composeArgs stop cwi-mockserver | Tee-Object -FilePath (Join-Path $runDir "mockserver-down.log")
  Assert-LastExit "stop mockserver"
  Start-Sleep -Seconds 5
  try {
    $env:CWI_EXPECT_MODEL_PRO_DOWN = "1"
    Invoke-PytestSubset -TestPath "tools/cwi-gold-tests/tests/test_simulation.py" -Filter "model_down" -RunDir $runDir -Label "simulation-model-pro-down"
  } finally {
    $env:CWI_EXPECT_MODEL_PRO_DOWN = ""
    & docker compose @composeArgs start cwi-mockserver | Tee-Object -FilePath (Join-Path $runDir "mockserver-up.log")
    Assert-LastExit "start mockserver"
  }

  Write-Section "Simulation: mTLS rotation"
  Invoke-PytestSubset -TestPath "tools/cwi-gold-tests/tests/test_mtls_rotation.py" -Filter "mtls_rotation" -RunDir $runDir -Label "simulation-mtls-rotation"

  Write-Section "Capture compose logs"
  & docker compose @composeArgs logs --no-color | Tee-Object -FilePath (Join-Path $runDir "compose-logs.log")
  Assert-LastExit "compose logs"

  Write-Host "Run complete. Artifacts written to $runDir"
}
finally {
  if (-not $KeepRunning) {
    Write-Section "Shutdown compose stack"
    try {
      & docker compose @composeArgs down | Tee-Object -FilePath (Join-Path $runDir "compose-down-final.log")
      Assert-LastExit "docker compose down (final)"
    } catch {
      Write-Host "Failed to shutdown compose stack: $($_.Exception.Message)"
    }
  }
  Pop-Location
}
