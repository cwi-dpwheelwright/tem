param(
  [Parameter(Mandatory = $true)]
  [string]$Path
)

$ErrorActionPreference = "Stop"

$safePlaceholders = @(
  "",
  "changeme",
  "redacted",
  "placeholder",
  "example",
  "your_value",
  "your-token",
  "your_secret",
  "your-secret"
)

$suspiciousKeys = [regex]::new("(secret|token|password|apikey|api_key|key)", "IgnoreCase")

function Test-SafeValue {
  param([string]$Value)

  $lowered = $Value.Trim().ToLowerInvariant()
  if ($safePlaceholders -contains $lowered) {
    return $true
  }
  if ($lowered.StartsWith("change_me")) {
    return $true
  }
  if ($lowered -match '^[0-9]+$') {
    return $true
  }
  if ($lowered.StartsWith("<") -and $lowered.EndsWith(">")) {
    return $true
  }
  if ($lowered.StartsWith("\${") -and $lowered.EndsWith("}")) {
    return $true
  }
  return $false
}

function Get-EnvTemplateFiles {
  param([string]$TargetPath)

  if (-not (Test-Path -LiteralPath $TargetPath)) {
    throw "Path not found: $TargetPath"
  }

  if (Test-Path -LiteralPath $TargetPath -PathType Container) {
    return Get-ChildItem -LiteralPath $TargetPath -Recurse -Filter "*.env.template" -File
  }

  return Get-Item -LiteralPath $TargetPath
}

$findings = New-Object System.Collections.Generic.List[string]

foreach ($file in Get-EnvTemplateFiles -TargetPath $Path) {
  $lineNumber = 0
  Get-Content -LiteralPath $file.FullName | ForEach-Object {
    $lineNumber++
    $line = $_.Trim()
    if (-not $line -or $line.StartsWith("#")) {
      return
    }
    $parts = $line -split "=", 2
    if ($parts.Count -lt 2) {
      return
    }
    $key = $parts[0].Trim()
    $value = $parts[1]
    $normalizedKey = $key.ToUpperInvariant()
    if (
      $normalizedKey.EndsWith("_DIR") -or
      $normalizedKey.EndsWith("_PATH") -or
      $normalizedKey.EndsWith("_URL") -or
      $normalizedKey.EndsWith("_ISSUER") -or
      $normalizedKey.EndsWith("_AUDIENCE")
    ) {
      return
    }
    if ($suspiciousKeys.IsMatch($key) -and -not (Test-SafeValue -Value $value)) {
      $findings.Add("  - $($file.FullName):${lineNumber}: $key") | Out-Null
    }
  }
}

if ($findings.Count -gt 0) {
  Write-Host "Potential secrets detected:"
  $findings | ForEach-Object { Write-Host $_ }
  exit 1
}

Write-Host "OK"
