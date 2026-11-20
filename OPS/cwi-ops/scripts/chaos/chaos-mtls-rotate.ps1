param(
  [Parameter(Mandatory = $false)]
  [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
  [Parameter(Mandatory = $false)]
  [string]$Service,
  [Parameter(Mandatory = $false)]
  [string]$Container,
  [Parameter(Mandatory = $true)]
  [string]$LocalBundlePath,
  [Parameter(Mandatory = $false)]
  [string]$ContainerBundlePath = "/etc/cwi/pki/trust-bundle.pem",
  [Parameter(Mandatory = $false)]
  [string]$LocalCrlPath,
  [Parameter(Mandatory = $false)]
  [string]$ContainerCrlPath = "/etc/cwi/pki/crl.pem",
  [Parameter(Mandatory = $false)]
  [switch]$Restart
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $LocalBundlePath)) {
  throw "Local bundle not found: $LocalBundlePath"
}

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

Write-Host "Copying trust bundle to $Container:$ContainerBundlePath"
docker cp $LocalBundlePath "${Container}:$ContainerBundlePath" | Out-Host

if ($LocalCrlPath) {
  if (-not (Test-Path $LocalCrlPath)) {
    throw "Local CRL not found: $LocalCrlPath"
  }
  Write-Host "Copying CRL to $Container:$ContainerCrlPath"
  docker cp $LocalCrlPath "${Container}:$ContainerCrlPath" | Out-Host
}

if ($Restart) {
  Write-Host "Restarting $Container..."
  docker restart $Container | Out-Host
} else {
  Write-Host "Bundle updated. Restart or reload the service to apply."
}
