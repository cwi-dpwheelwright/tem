param(
  [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
  [string]$EnvFile = "OPS/cwi-ops/compose/test/.env",
  [string]$Profile = "integration",
  [int]$StartupWaitSeconds = 90
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $ComposeFile)) {
  Write-Host "Compose file not found: $ComposeFile"
  exit 1
}

if (-not (Test-Path $EnvFile)) {
  Write-Host "Env file not found: $EnvFile"
  exit 1
}

function Read-EnvFile([string]$path) {
  $map = @{}
  Get-Content $path | ForEach-Object {
    $line = $_.Trim()
    if (-not $line -or $line.StartsWith("#")) { return }
    $parts = $line.Split("=", 2)
    if ($parts.Count -eq 2) {
      $map[$parts[0]] = $parts[1]
    }
  }
  return $map
}

$envMap = Read-EnvFile $EnvFile
$orchestratorPort = if ($envMap.ContainsKey("ORCHESTRATOR_HOST_PORT")) { $envMap["ORCHESTRATOR_HOST_PORT"] } else { "18085" }

try {
  docker compose -f $ComposeFile --env-file $EnvFile --profile $Profile up -d

  Write-Host "Waiting for orchestrator health..."
  $deadline = (Get-Date).AddSeconds($StartupWaitSeconds)
  $ready = $false
  while ((Get-Date) -lt $deadline) {
    try {
      $health = Invoke-RestMethod -TimeoutSec 2 -Uri "http://localhost:$orchestratorPort/api/health"
      if ($health.status -eq "ok") {
        $ready = $true
        break
      }
    } catch {
    }
    Start-Sleep -Seconds 2
  }

  if (-not $ready) {
    Write-Host "Orchestrator health check did not succeed within $StartupWaitSeconds seconds."
    exit 1
  }

  foreach ($key in $envMap.Keys) {
    Set-Item -Path "env:$key" -Value $envMap[$key]
  }

  if (-not $env:CWI_TENANT_ID) {
    if ($env:DEFAULT_TENANT_ID) {
      $env:CWI_TENANT_ID = $env:DEFAULT_TENANT_ID
    } else {
      $env:CWI_TENANT_ID = "tenant-01"
    }
  }

  if (-not $env:CWI_BREAK_GLASS_ISSUE_URL) {
    $env:CWI_BREAK_GLASS_ISSUE_URL = "http://localhost:$orchestratorPort/api/break-glass/issue"
  }
  if (-not $env:CWI_BREAK_GLASS_ROLE) { $env:CWI_BREAK_GLASS_ROLE = "admin" }
  if (-not $env:CWI_BREAK_GLASS_SCOPE) { $env:CWI_BREAK_GLASS_SCOPE = "admin" }
  if (-not $env:CWI_BREAK_GLASS_USER_ID) { $env:CWI_BREAK_GLASS_USER_ID = "ci@example.com" }
  if (-not $env:CWI_BREAK_GLASS_DURATION_MINUTES) { $env:CWI_BREAK_GLASS_DURATION_MINUTES = "5" }
  if (-not $env:CWI_BREAK_GLASS_REASON) { $env:CWI_BREAK_GLASS_REASON = "ci-kill-switch-sync" }
  if (-not $env:CWI_KILL_SWITCH_ADMIN_URL) {
    $env:CWI_KILL_SWITCH_ADMIN_URL = "http://localhost:$orchestratorPort/api/admin/kill_switch"
  }

  python -m pip install -e src/node-agent-runtime
  python -m pytest src/node-agent-runtime/tests/test_kill_switch_integration.py
}
finally {
  docker compose -f $ComposeFile --env-file $EnvFile --profile $Profile down
}
