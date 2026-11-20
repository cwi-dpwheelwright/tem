param(
  [string]$OutputDir = "artifacts/sbom"
)

$ErrorActionPreference = "Stop"

$root = (Get-Item -Path ".").FullName
$outputPath = Join-Path $root $OutputDir
if (-not (Test-Path $outputPath)) {
  New-Item -ItemType Directory -Path $outputPath | Out-Null
}

$targets = @(
  @{ Name = "tools-codegen"; Path = "tools/codegen" },
  @{ Name = "teams-bot"; Path = "src/backend/services/cwi-teams-bot" },
  @{ Name = "web-ui"; Path = "src/frontend/web-ui/cwi-glassbox-web-ui" },
  @{ Name = "gold-tests"; Path = "tools/cwi-gold-tests" },
  @{ Name = "android"; Path = "src/mobile/android" },
  @{ Name = "ios-carplay"; Path = "src/mobile/ios-carplay" }
)

foreach ($target in $targets) {
  $targetPath = Join-Path $root $target.Path
  if (-not (Test-Path $targetPath)) {
    Write-Host "Skipping missing path: $($target.Path)"
    continue
  }
  $outRel = (Join-Path $OutputDir "$($target.Name).cdx.json").Replace('\','/')
  $excludes = @(
    "**/.git/**",
    "**/.gradle/**",
    "**/.idea/**",
    "**/bin/**",
    "**/obj/**",
    "**/build/**",
    "**/dist/**",
    "**/node_modules/**",
    "**/out/**"
  )
  $excludeArgs = @()
  foreach ($exclude in $excludes) {
    $excludeArgs += @("--exclude", $exclude)
  }
  Write-Host "Generating SBOM for $($target.Path) -> $outRel"
  $dockerArgs = @(
    "run",
    "--rm",
    "-v", "${root}:/work",
    "-w", "/work",
    "anchore/syft:latest",
    "dir:/work/$($target.Path)",
    "-o", "cyclonedx-json=$outRel"
  ) + $excludeArgs
  & docker @dockerArgs
}
