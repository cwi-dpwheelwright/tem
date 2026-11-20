param(
  [Parameter(Mandatory = $false)]
  [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
  [Parameter(Mandatory = $false)]
  [string]$Service,
  [Parameter(Mandatory = $false)]
  [string]$Container,
  [Parameter(Mandatory = $false)]
  [string]$TargetPath = "/tmp",
  [Parameter(Mandatory = $false)]
  [int]$SizeMb = 256,
  [Parameter(Mandatory = $false)]
  [int]$DurationSeconds = 60
)

$ErrorActionPreference = "Stop"

if (-not $Container) {
  if (-not $Service) {
    throw "Provide -Service or -Container."
  }
  if (-not (Test-Path $ComposeFile)) {
    throw "Compose file not found: $ComposeFile"
  }
  $output = cmd /c "docker compose -f `"$ComposeFile`" ps -q $Service 2>&1"
  if ($LASTEXITCODE -ne 0) {
    throw "docker compose ps failed with exit code $LASTEXITCODE"
  }
  $Container = ($output -split "`r?`n" | Where-Object { $_ -match '^[0-9a-f]{12,}$' } | Select-Object -Last 1).Trim()
  if (-not $Container) {
    throw "No running container found for service $Service."
  }
}

$marker = "$TargetPath/chaos.fill"
Write-Host "Creating $SizeMb MB file at $marker inside $Container..."
docker exec $Container sh -c "dd if=/dev/zero of=$marker bs=1M count=$SizeMb >/dev/null 2>&1 || fallocate -l ${SizeMb}M $marker" | Out-Host

Start-Sleep -Seconds $DurationSeconds

Write-Host "Removing $marker from $Container..."
docker exec $Container sh -c "rm -f $marker" | Out-Host
