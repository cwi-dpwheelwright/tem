param(
  [string]$OutputDir = "artifacts/scans",
  [string]$Severity = "HIGH,CRITICAL"
)

$ErrorActionPreference = "Stop"

$root = (Get-Item -Path ".").FullName
$outputPath = Join-Path $root $OutputDir
if (-not (Test-Path $outputPath)) {
  New-Item -ItemType Directory -Path $outputPath | Out-Null
}
$dockerSock = "/var/run/docker.sock"

function Invoke-Docker {
  param([string]$Command)
  Write-Host $Command
  cmd /c $Command
  if ($LASTEXITCODE -ne 0) {
    throw "Command failed with exit code $LASTEXITCODE"
  }
}

Write-Host "Running Semgrep (SAST) ..."
$semgrepOut = (Join-Path $OutputDir "semgrep.json").Replace('\','/')
$semgrepCmd = @(
  "docker run --rm",
  "-v `"${root}:/src`"",
  "-w /src",
  "returntocorp/semgrep:latest",
  "semgrep --config auto --error --json -o $semgrepOut",
  "--exclude .git --exclude artifacts --exclude OPS/cwi-ops/compose --exclude OPS/cwi-ops/DEPLOY"
) -join " "
Invoke-Docker -Command $semgrepCmd

Write-Host "Running pip-audit ..."
$pipAuditOut = (Join-Path $OutputDir "pip-audit.json").Replace('\','/')
$pipAuditCmd = @(
  "docker run --rm",
  "-v `"${root}:/work`"",
  "-w /work/tools/cwi-gold-tests",
  "python:3.11-slim",
  "sh -lc `"pip install -q pip-audit && pip-audit --path . --format json -o /work/$pipAuditOut`""
) -join " "
Invoke-Docker -Command $pipAuditCmd

Write-Host "Running npm audit ..."
$npmTargets = @(
  @{ Path = "tools/codegen"; Out = "npm-audit-codegen.json" },
  @{ Path = "src/backend/services/cwi-teams-bot"; Out = "npm-audit-teams-bot.json" },
  @{ Path = "src/frontend/web-ui/cwi-glassbox-web-ui"; Out = "npm-audit-web-ui.json" }
)
foreach ($target in $npmTargets) {
  $auditOut = (Join-Path $OutputDir $target.Out).Replace('\','/')
  $npmCmd = @(
    "docker run --rm",
    "-v `"${root}:/work`"",
    "-w /work/$($target.Path)",
    "node:20-bullseye-slim",
    "sh -lc `"npm ci --ignore-scripts --no-fund --no-audit && npm audit --audit-level=high --json > /work/$auditOut`""
  ) -join " "
  Invoke-Docker -Command $npmCmd
}

Write-Host "Running .NET vulnerability scan ..."
$dotnetOut = Join-Path $outputPath "dotnet-vulnerable.txt"
$dotnetResults = New-Object System.Collections.Generic.List[string]
$solutions = @(
  "src/backend/services/cwi-router/cwi-router.sln",
  "src/backend/services/cwi-orchestrator/cwi-orchestrator.sln"
)
foreach ($sln in $solutions) {
  Write-Host "Scanning $sln"
  $output = dotnet list $sln package --vulnerable --include-transitive 2>&1
  $dotnetResults.Add($output) | Out-Null
  if ($output -match "has the following vulnerable packages") {
    throw "Vulnerable packages detected in $sln"
  }
}
Set-Content -Path $dotnetOut -Value ($dotnetResults -join "`n`n")

Write-Host "Running Trivy filesystem scan ..."
$trivyFsOut = (Join-Path $OutputDir "trivy-fs.json").Replace('\','/')
$trivyFsCmd = @(
  "docker run --rm",
  "-v `"${root}:/work`"",
  "-w /work",
  "aquasec/trivy:latest",
  "fs --scanners vuln --timeout 30m --exit-code 1 --severity $Severity --format json --output /work/$trivyFsOut --ignorefile /work/.trivyignore",
  "--skip-dirs .git,artifacts,.github,node_modules,src/mobile/android",
  "/work"
) -join " "
Invoke-Docker -Command $trivyFsCmd

Write-Host "Running Trivy image scans ..."
$images = @(
  @{ Image = "local/cwi-router:10h"; Out = "trivy-image-router.json" },
  @{ Image = "local/cwi-orchestrator:10h"; Out = "trivy-image-orchestrator.json" }
)
foreach ($img in $images) {
  $trivyImgOut = (Join-Path $OutputDir $img.Out).Replace('\','/')
  $trivyImgCmd = @(
    "docker run --rm",
    "-v `"${root}:/work`"",
    "-v `"${dockerSock}:/var/run/docker.sock`"",
    "aquasec/trivy:latest",
    "image --scanners vuln --timeout 20m --exit-code 1 --severity $Severity --format json --output /work/$trivyImgOut --ignorefile /work/.trivyignore",
    "$($img.Image)"
  ) -join " "
  Invoke-Docker -Command $trivyImgCmd
}

Write-Host "Security scans complete."
