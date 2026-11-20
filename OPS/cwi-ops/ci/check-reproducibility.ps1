param(
  [string]$RepoRoot = "."
)

$ErrorActionPreference = "Stop"

Write-Host "Checking lockfiles..."
$packageJsons = Get-ChildItem -Path $RepoRoot -Recurse -Filter package.json | Where-Object {
  $_.FullName -notmatch "\\node_modules\\" -and
  $_.FullName -notmatch "\\.next\\" -and
  $_.FullName -notmatch "\\dist\\"
}
foreach ($pkg in $packageJsons) {
  $lock = Join-Path $pkg.Directory.FullName "package-lock.json"
  if (-not (Test-Path $lock)) {
    throw "Missing package-lock.json for $($pkg.FullName)"
  }
}

$csprojFiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter *.csproj
foreach ($proj in $csprojFiles) {
  $lock = Join-Path $proj.Directory.FullName "packages.lock.json"
  if (-not (Test-Path $lock)) {
    throw "Missing packages.lock.json for $($proj.FullName)"
  }
}

Write-Host "Checking Dockerfile base images..."
$dockerfiles = Get-ChildItem -Path $RepoRoot -Recurse -Filter Dockerfile*
foreach ($file in $dockerfiles) {
  $lines = Get-Content $file.FullName
  foreach ($line in $lines) {
    if ($line -match '^\s*FROM\s+(\S+)') {
      $image = $Matches[1]
      if ($image -match "^[^:]+$" -and $image -notmatch "@sha256:") {
        throw "Unpinned base image in $($file.FullName): $image"
      }
      if ($image -match ":latest$") {
        throw "Latest tag not allowed in $($file.FullName): $image"
      }
    }
  }
}

Write-Host "Reproducibility checks passed."
