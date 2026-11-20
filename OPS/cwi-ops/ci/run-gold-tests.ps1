param(
  [string]$GoldTestsRoot = "tools/cwi-gold-tests",
  [string]$BaseUrl = "http://localhost:8081",
  [string]$CoordinationUrl = "",
  [string]$CoordinationAdminToken = "",
  [string]$CoordinationAdminHeader = "",
  [string]$CoordinationRequireAdmin = ""
)

$ErrorActionPreference = "Stop"

$runner = Join-Path $GoldTestsRoot "run_tests.py"
if (-not (Test-Path $runner)) {
  Write-Host "Gold tests runner not found at $runner"
  exit 1
}

$pythonOk = $false
$pythonVersionOk = $false
try {
  $versionText = (& python --version 2>$null)
  $pythonOk = $true
  if ($versionText -match "Python\s+(\d+)\.(\d+)\.") {
    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    if ($major -gt 3 -or ($major -eq 3 -and $minor -ge 10)) {
      $pythonVersionOk = $true
    }
  }
} catch {
  $pythonOk = $false
}

if ($pythonOk -and $pythonVersionOk) {
  $env:PIP_DISABLE_PIP_VERSION_CHECK = "1"
  $env:PIP_NO_WARN_SCRIPT_LOCATION = "1"
  $env:PIP_ROOT_USER_ACTION = "ignore"
  python -m pip install -q pytest requests schemathesis httpx jsonschema pyyaml defusedxml
  if ($CoordinationUrl) { $env:CWI_COORDINATION_URL = $CoordinationUrl }
  if ($CoordinationAdminToken) { $env:CWI_COORDINATION_ADMIN_TOKEN = $CoordinationAdminToken }
  if ($CoordinationAdminHeader) { $env:CWI_COORDINATION_ADMIN_HEADER = $CoordinationAdminHeader }
  if ($CoordinationRequireAdmin) { $env:CWI_COORDINATION_REQUIRE_ADMIN = $CoordinationRequireAdmin }
  python $runner --suite smoke --base-url $BaseUrl
  exit $LASTEXITCODE
}

Write-Host "Python not available; running gold tests in docker."
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\\..\\.."))
$containerBaseUrl = $BaseUrl
if ($containerBaseUrl -match "localhost") {
  $containerBaseUrl = $containerBaseUrl -replace "localhost", "cwi-gateway"
}
if ($containerBaseUrl -match "127\\.0\\.0\\.1") {
  $containerBaseUrl = $containerBaseUrl -replace "127\\.0\\.0\\.1", "cwi-gateway"
}

docker run --rm --network cwi-test `
  -e PIP_DISABLE_PIP_VERSION_CHECK=1 `
  -e PIP_NO_WARN_SCRIPT_LOCATION=1 `
  -e PIP_ROOT_USER_ACTION=ignore `
  -e CWI_BASE_URL=$containerBaseUrl `
  -e CWI_COORDINATION_URL=$CoordinationUrl `
  -e CWI_COORDINATION_ADMIN_TOKEN=$CoordinationAdminToken `
  -e CWI_COORDINATION_ADMIN_HEADER=$CoordinationAdminHeader `
  -e CWI_COORDINATION_REQUIRE_ADMIN=$CoordinationRequireAdmin `
  -v "${repoRoot}:/repo" `
  -w /repo/tools/cwi-gold-tests `
  python:3.11-slim `
  bash -lc "python -m pip install -q pytest requests schemathesis httpx jsonschema pyyaml defusedxml && python run_tests.py --suite smoke --base-url $containerBaseUrl"
