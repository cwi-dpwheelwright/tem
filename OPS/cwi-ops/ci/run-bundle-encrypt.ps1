param(
  [string]$InputDir = "artifacts/sbom",
  [string]$OutputDir = "artifacts/bundles",
  [string]$BundleName = "cwi-bundle"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:BUNDLE_ENCRYPTION_KEY)) {
  throw "BUNDLE_ENCRYPTION_KEY env var is required for bundle encryption."
}

$openssl = Get-Command openssl -ErrorAction SilentlyContinue
if (-not $openssl) {
  throw "openssl is required for bundle encryption."
}

$root = (Get-Item -Path ".").FullName
$inputPath = Join-Path $root $InputDir
$outputPath = Join-Path $root $OutputDir

if (-not (Test-Path $inputPath)) {
  throw "Input directory not found: $inputPath"
}

if (-not (Test-Path $outputPath)) {
  New-Item -ItemType Directory -Path $outputPath | Out-Null
}

$tarPath = Join-Path $outputPath "$BundleName.tar"
$encPath = Join-Path $outputPath "$BundleName.enc"
$manifestPath = Join-Path $outputPath "$BundleName.manifest.json"

if (Test-Path $tarPath) { Remove-Item $tarPath -Force }
if (Test-Path $encPath) { Remove-Item $encPath -Force }

Write-Host "Creating bundle tar: $tarPath"
tar -cf $tarPath -C $inputPath .

Write-Host "Encrypting bundle -> $encPath"
openssl enc -aes-256-gcm -salt -pbkdf2 `
  -pass env:BUNDLE_ENCRYPTION_KEY `
  -in $tarPath -out $encPath

$hash = (Get-FileHash -Path $encPath -Algorithm SHA256).Hash.ToLowerInvariant()
@{
  bundle = "$BundleName.enc"
  sha256 = $hash
  created_utc = (Get-Date).ToUniversalTime().ToString("o")
} | ConvertTo-Json -Depth 3 | Set-Content -Path $manifestPath -Encoding UTF8

Write-Host "Bundle manifest written to $manifestPath"
