param(
  [string]$Registry = "registry.example.com",
  [string]$RouterTag = "10h",
  [string]$OrchestratorTag = "10h",
  [string]$OutputDir = "artifacts/sbom"
)

$ErrorActionPreference = "Stop"

$root = (Get-Item -Path ".").FullName
$outputPath = Join-Path $root $OutputDir
if (-not (Test-Path $outputPath)) {
  New-Item -ItemType Directory -Path $outputPath | Out-Null
}

$routerImage = "$Registry/cwi-router:$RouterTag"
$orchestratorImage = "$Registry/cwi-orchestrator:$OrchestratorTag"
$routerSbom = Join-Path $outputPath "cwi-router.cdx.json"
$orchestratorSbom = Join-Path $outputPath "cwi-orchestrator.cdx.json"
$dockerSock = "/var/run/docker.sock"
if ($IsWindows) {
  $dockerSock = "//./pipe/docker_engine"
}
$routerSbomRel = (Join-Path $OutputDir "cwi-router.cdx.json").Replace('\','/')
$orchestratorSbomRel = (Join-Path $OutputDir "cwi-orchestrator.cdx.json").Replace('\','/')

Write-Host "Generating SBOM for $routerImage -> $routerSbom"
Push-Location $root
docker run --rm `
  -v "${dockerSock}:/var/run/docker.sock" `
  -v "${root}:/work" `
  -w /work `
  anchore/syft:latest `
  $routerImage -o "cyclonedx-json=$routerSbomRel"

Write-Host "Generating SBOM for $orchestratorImage -> $orchestratorSbom"
docker run --rm `
  -v "${dockerSock}:/var/run/docker.sock" `
  -v "${root}:/work" `
  -w /work `
  anchore/syft:latest `
  $orchestratorImage -o "cyclonedx-json=$orchestratorSbomRel"
Pop-Location
