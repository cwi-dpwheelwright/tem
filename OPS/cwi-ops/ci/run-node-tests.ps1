param(
  [string]$ResultsDir = "artifacts/test-results/node"
)

$ErrorActionPreference = "Stop"

$resultsPath = Resolve-Path -Path $ResultsDir -ErrorAction SilentlyContinue
if (-not $resultsPath) {
  New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
  $resultsPath = Resolve-Path -Path $ResultsDir
}

function Get-PackageScripts {
  param([string]$ProjectPath)
  $pkgPath = Join-Path $ProjectPath "package.json"
  if (-not (Test-Path $pkgPath)) {
    return @{}
  }
  try {
    $pkg = Get-Content -LiteralPath $pkgPath -Raw | ConvertFrom-Json
    if ($null -eq $pkg.scripts) {
      return @{}
    }
    return $pkg.scripts.PSObject.Properties.Name
  } catch {
    return @{}
  }
}

function Invoke-NpmIfPresent {
  param(
    [string]$ProjectPath,
    [string]$Script,
    [string[]]$Args = @()
  )
  $scripts = Get-PackageScripts -ProjectPath $ProjectPath
  if ($scripts -contains $Script) {
    Write-Host "Running $Script for $ProjectPath"
    if ($Args.Count -gt 0) {
      npm --prefix $ProjectPath run $Script -- @Args
    } else {
      npm --prefix $ProjectPath run $Script
    }
  } else {
    Write-Host "Skipping missing script '$Script' for $ProjectPath"
  }
}

$projects = @(
  @{ Path = "src/frontend/web-ui/cwi-glassbox-web-ui"; TestScript = "test:unit" },
  @{ Path = "src/backend/services/cwi-teams-bot"; TestScript = "test" }
)

foreach ($project in $projects) {
  $path = $project.Path
  if (-not (Test-Path $path)) {
    Write-Host "Skipping missing $path"
    continue
  }

  $projectName = ($path -replace '[\\/]', '_')
  $junitPath = Join-Path $resultsPath "$projectName.junit.xml"

  Write-Host "Installing deps for $path"
  npm --prefix $path install

  Invoke-NpmIfPresent -ProjectPath $path -Script "lint"
  Invoke-NpmIfPresent -ProjectPath $path -Script "typecheck"

  Write-Host "Running tests for $path"
  $env:PLAYWRIGHT_JUNIT_OUTPUT_FILE = $junitPath
  if ($project.TestScript -eq "test:unit") {
    Invoke-NpmIfPresent -ProjectPath $path -Script $project.TestScript -Args @("--reporter=junit", "--outputFile", $junitPath)
  } else {
    Invoke-NpmIfPresent -ProjectPath $path -Script $project.TestScript -Args @("--reporter=junit", "--outputFile", $junitPath)
  }
  Remove-Item Env:\PLAYWRIGHT_JUNIT_OUTPUT_FILE -ErrorAction SilentlyContinue

  if (-not (Test-Path $junitPath)) {
    Write-Host "JUnit report not found at $junitPath"
  }
}
