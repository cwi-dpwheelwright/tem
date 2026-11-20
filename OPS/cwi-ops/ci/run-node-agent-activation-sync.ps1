param(
  [int]$Port = 18999,
  [int]$Iterations = 2,
  [int]$IntervalSeconds = 2
)

$ErrorActionPreference = "Stop"

$mockScript = "OPS/cwi-ops/scripts/mock-activation-status.py"
if (-not (Test-Path $mockScript)) {
  Write-Host "Mock activation status script not found: $mockScript"
  exit 1
}

$process = $null
try {
  $process = Start-Process -FilePath python -ArgumentList "$mockScript --host 127.0.0.1 --port $Port" -PassThru
  Start-Sleep -Seconds 2

  $configPath = "artifacts/activation-sync/node-agent.yaml"
  New-Item -ItemType Directory -Force (Split-Path $configPath) | Out-Null

  @"
coordinator_url: http://127.0.0.1:$Port
enrollment_url: http://127.0.0.1:$Port/api/membership/enroll
identity:
  node_id: node-01
  persist_keys: false
storage:
  state_dir: ./state
activation:
  status_url: http://127.0.0.1:$Port/api/activation/status
"@ | Set-Content -Path $configPath

  python -m pip install -e src/node-agent-runtime

  for ($i = 0; $i -lt $Iterations; $i++) {
    python -m node_agent_runtime.cli sync-activation-status --config $configPath
    if ($i -lt ($Iterations - 1)) {
      Start-Sleep -Seconds $IntervalSeconds
    }
  }

  $statusPath = Join-Path (Split-Path $configPath) "state\\activation\\remote_status.json"
  if (-not (Test-Path $statusPath)) {
    Write-Host "Remote status not written to $statusPath"
    exit 1
  }
}
finally {
  if ($process -and -not $process.HasExited) {
    Stop-Process -Id $process.Id -Force
  }
}
