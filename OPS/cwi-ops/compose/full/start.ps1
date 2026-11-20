param(
  [string]$ComposeFile = "docker-compose.full.yml",
  [string]$EnvFile = ".env",
  [switch]$NoBuild
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$composePath = Join-Path $scriptRoot $ComposeFile
$envPath = Join-Path $scriptRoot $EnvFile
$envTemplate = Join-Path $scriptRoot ".env.template"

if (-not (Test-Path $envPath)) {
  Copy-Item $envTemplate $envPath
  Write-Host "Created $envPath from .env.template"
}

if ($NoBuild) {
  $buildArg = ""
} else {
  $buildArg = "--build"
}
$cmd = "docker compose -f `"$composePath`" --env-file `"$envPath`" up -d $buildArg"
Write-Host $cmd
Invoke-Expression $cmd
