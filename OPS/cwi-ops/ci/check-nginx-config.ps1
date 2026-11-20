param(
  [string[]]$ConfigPaths = @(
    "OPS/cwi-ops/compose/pack-service/nginx.conf",
    "OPS/cwi-ops/DEPLOY/pack-service/nginx.conf"
  )
)

$ErrorActionPreference = "Stop"

foreach ($config in $ConfigPaths) {
  if (-not (Test-Path $config)) {
    throw "nginx.conf not found: $config"
  }

  $content = Get-Content -Path $config -Raw
  if ($content -notmatch "location\\s+/packs/") {
    throw "nginx.conf missing /packs/ location: $config"
  }

  $root = (Get-Item -Path ".").FullName
  $configPath = Join-Path $root $config
  Write-Host "Validating nginx config: $config"

  docker run --rm `
    -v "${configPath}:/etc/nginx/nginx.conf:ro" `
    nginx:1.25 `
    nginx -t
}
