param(
  [string]$PolicyPath = "OPS/cwi-ops/ci/license-policy.json",
  [string[]]$NodeProjects = @(
    "src/frontend/web-ui/cwi-glassbox-web-ui",
    "src/backend/services/cwi-teams-bot"
  )
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PolicyPath)) {
  throw "License policy not found: $PolicyPath"
}

$policy = Get-Content $PolicyPath -Raw | ConvertFrom-Json
$allowed = @($policy.allowed)
$blocked = @($policy.blocked)
$exceptions = @($policy.exceptions)

$npx = Get-Command npx -ErrorAction SilentlyContinue
$npm = Get-Command npm -ErrorAction SilentlyContinue
if (-not $npx -and -not $npm) {
  $knownNpx = "C:\\Program Files\\nodejs\\npx.cmd"
  $knownNpm = "C:\\Program Files\\nodejs\\npm.cmd"
  $knownNodeDir = "C:\\Program Files\\nodejs"
  if (Test-Path $knownNpx) {
    $npx = $knownNpx
  }
  if (Test-Path $knownNpm) {
    $npm = $knownNpm
  }
  if (Test-Path $knownNodeDir) {
    $env:PATH = "$knownNodeDir;$env:PATH"
  }
}
if (-not $npx -and -not $npm) {
  throw "npx or npm is required to run license checks."
}

function Normalize-License([string]$license) {
  if (-not $license) { return @() }
  $normalized = $license -replace "[()]", "" -replace "\*", "" -replace " / ", " OR " -replace " AND ", " OR "
  return $normalized.Split(@(" OR ", " | "), [System.StringSplitOptions]::RemoveEmptyEntries) |
    ForEach-Object { $_.Trim() }
}

function Get-PackageName([string]$name) {
  if ($name.StartsWith("@")) {
    $lastAt = $name.LastIndexOf("@")
    if ($lastAt -gt 0) {
      return $name.Substring(0, $lastAt)
    }
  } elseif ($name.Contains("@")) {
    return $name.Split("@")[0]
  }
  return $name
}

function Get-Exception([string]$name) {
  $pkgName = Get-PackageName $name
  foreach ($ex in $exceptions) {
    if ($ex.package -and $ex.package -eq $pkgName) {
      return $ex
    }
    if ($ex.package_prefix -and $pkgName.StartsWith($ex.package_prefix)) {
      return $ex
    }
  }
  return $null
}

function Check-LicenseList([string[]]$licenses, [string]$name) {
  $exception = Get-Exception $name
  if ($exception) {
    $allowedByException = @($exception.licenses)
    foreach ($lic in $licenses) {
      if (-not ($allowedByException -contains $lic)) {
        throw "License $lic not covered by exception for $name"
      }
    }
    return
  }
  foreach ($lic in $licenses) {
    if ($blocked -contains $lic) {
      throw "Blocked license detected ($lic) in $name"
    }
  }
  if ($allowed.Count -gt 0) {
    foreach ($lic in $licenses) {
      if (-not ($allowed -contains $lic)) {
        throw "Non-allowed license detected ($lic) in $name"
      }
    }
  }
}

foreach ($project in $NodeProjects) {
  if (-not (Test-Path $project)) {
    Write-Host "Skipping missing $project"
    continue
  }
  Write-Host "Checking node licenses for $project"
  if ($npx) {
    $json = & $npx --yes license-checker --json --production --start $project | ConvertFrom-Json
  } else {
    $json = & $npm exec --yes -- license-checker --json --production --start $project | ConvertFrom-Json
  }
  foreach ($pkg in $json.PSObject.Properties) {
    $licenses = Normalize-License $pkg.Value.licenses
    if ($licenses.Count -eq 0) { continue }
    Check-LicenseList $licenses $pkg.Name
  }
}

Write-Host "License check passed."
