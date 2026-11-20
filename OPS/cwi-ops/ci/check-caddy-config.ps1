param(
  [string[]]$ConfigPaths = @(
    "OPS/cwi-ops/compose/gateway/Caddyfile",
    "OPS/cwi-ops/compose/gateway/Caddyfile.mtls"
  )
)

$ErrorActionPreference = "Stop"

foreach ($config in $ConfigPaths) {
  if (-not (Test-Path $config)) {
    throw "Caddyfile not found: $config"
  }

  $root = (Get-Item -Path ".").FullName
  $configPath = Join-Path $root $config
  Write-Host "Validating Caddyfile: $config"

  docker run --rm `
    -v "${configPath}:/etc/caddy/Caddyfile:ro" `
    caddy:2 `
    caddy validate --config /etc/caddy/Caddyfile
}
