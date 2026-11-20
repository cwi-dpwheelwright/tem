param(
  [Parameter(Mandatory = $true)]
  [string]$Role,
  [Parameter(Mandatory = $true)]
  [string]$EnvFile,
  [string]$Matrix = "OPS/cwi-ops/env/ENV_VAR_MATRIX.json",
  [switch]$AllowExtra
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $Matrix)) {
  throw "Env matrix not found: $Matrix"
}

if (-not (Test-Path -LiteralPath $EnvFile)) {
  throw "Env file not found: $EnvFile"
}

$matrixJson = Get-Content -LiteralPath $Matrix -Raw | ConvertFrom-Json
$roles = $matrixJson.roles
if ($null -eq $roles.$Role) {
  throw "Unknown role: $Role"
}

$required = @($roles.$Role.required) | ForEach-Object { $_.ToString() }
$optional = @()
if ($null -ne $roles.$Role.optional) {
  $optional = @($roles.$Role.optional) | ForEach-Object { $_.ToString() }
}
$envVars = @{}
Get-Content -LiteralPath $EnvFile | ForEach-Object {
  $line = $_.Trim()
  if (-not $line -or $line.StartsWith("#")) {
    return
  }
  if ($line -notmatch "=") {
    throw "Invalid line in ${EnvFile}: $line"
  }
  $parts = $line -split "=", 2
  $envVars[$parts[0].Trim()] = $parts[1].Trim()
}

$present = $envVars.Keys
$allowed = $required + $optional
$missing = $required | Where-Object { $_ -notin $present } | Sort-Object
$extra = $present | Where-Object { $_ -notin $allowed } | Sort-Object

$failed = $false
if ($missing.Count -gt 0) {
  Write-Host "Missing required vars:"
  $missing | ForEach-Object { Write-Host "  - $_" }
  $failed = $true
}

if ($extra.Count -gt 0 -and -not $AllowExtra) {
  Write-Host "Unexpected vars (not in required/optional; use -AllowExtra to ignore):"
  $extra | ForEach-Object { Write-Host "  - $_" }
  $failed = $true
}

if ($failed) {
  exit 1
}

Write-Host "OK"
