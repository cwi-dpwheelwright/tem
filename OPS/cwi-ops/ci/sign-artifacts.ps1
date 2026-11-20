param(
  [string]$InputDir = "artifacts/sbom",
  [string]$OutputDir = "artifacts/signatures"
)

$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($env:COSIGN_PRIVATE_KEY)) {
  throw "COSIGN_PRIVATE_KEY env var is required for artifact signing."
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

$files = Get-ChildItem -Path $inputPath -File
if ($files.Count -eq 0) {
  throw "No artifacts found to sign in $inputPath"
}

Push-Location $root
foreach ($file in $files) {
  $baseName = $file.BaseName
  $bundlePath = Join-Path $outputPath "$baseName.bundle.json"
  $bundleRel = (Join-Path $OutputDir "$baseName.bundle.json").Replace('\','/')
  $fileRel = (Resolve-Path -Relative $file.FullName).Replace('\','/')

  Write-Host "Signing $($file.FullName) -> $bundlePath"
  docker run --rm `
    -v "${root}:/work" `
    -w /work `
    -e COSIGN_PRIVATE_KEY `
    -e COSIGN_PASSWORD `
    ghcr.io/sigstore/cosign/cosign:latest `
    sign-blob --yes --key env://COSIGN_PRIVATE_KEY `
    --bundle $bundleRel `
    $fileRel
}
Pop-Location
