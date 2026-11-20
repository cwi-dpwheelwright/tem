param(
  [Parameter(Mandatory = $false)]
  [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
  [Parameter(Mandatory = $false)]
  [string]$Service,
  [Parameter(Mandatory = $false)]
  [string]$Container,
  [Parameter(Mandatory = $false)]
  [int]$LatencyMs = 250,
  [Parameter(Mandatory = $false)]
  [int]$LossPercent = 10,
  [Parameter(Mandatory = $false)]
  [string]$Interface = "eth0",
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
  $Container = (docker compose -f $ComposeFile ps -q $Service).Trim()
  if (-not $Container) {
    throw "No running container found for service $Service."
  }
}

$netemArgs = "delay ${LatencyMs}ms loss ${LossPercent}%"
Write-Host "Injecting netem on $Container ($Interface): $netemArgs for $DurationSeconds seconds..."
docker run --rm --network "container:$Container" --cap-add NET_ADMIN alpine sh -c `
  "apk add --no-cache iproute2 >/dev/null && tc qdisc add dev $Interface root netem $netemArgs" | Out-Host

Start-Sleep -Seconds $DurationSeconds

Write-Host "Removing netem on $Container ($Interface)..."
docker run --rm --network "container:$Container" --cap-add NET_ADMIN alpine sh -c `
  "apk add --no-cache iproute2 >/dev/null && tc qdisc del dev $Interface root" | Out-Host
