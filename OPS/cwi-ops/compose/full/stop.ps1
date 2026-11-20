param(
  [string]$ComposeFile = "docker-compose.full.yml",
  [string]$EnvFile = ".env"
)

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$composePath = Join-Path $scriptRoot $ComposeFile
$envPath = Join-Path $scriptRoot $EnvFile

$cmd = "docker compose -f `"$composePath`" --env-file `"$envPath`" down"
Write-Host $cmd
Invoke-Expression $cmd
