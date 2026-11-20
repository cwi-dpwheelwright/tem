param(
  [string]$InventoryPath = "OPS/cwi-ops/DEPLOY/deploy.inventory.local.json",
  [string]$GatewayEnvFile = "OPS/cwi-ops/DEPLOY/public-gateway.env",
  [string]$CoreEnvFile = "OPS/cwi-ops/DEPLOY/mcp-core/.env",
  [string]$TeacherEnvFile = "OPS/cwi-ops/DEPLOY/teacher_node/.env",
  [switch]$Deploy,
  [switch]$DryRun,
  [switch]$UseHttpsGateway,
  [switch]$SkipRemoteChecks,
  [switch]$SkipFullStackSmoke,
  [switch]$SkipPackChecks,
  [switch]$SkipVectorChecks,
  [switch]$SkipGoldTests,
  [switch]$SkipLedgerVerify,
  [switch]$SkipAuth,
  [switch]$SkipPackMirror,
  [switch]$SkipTlsVerification,
  [string]$GatewayBaseUrl,
  [string]$IdentityBaseUrl,
  [string]$PackServiceBaseUrl,
  [string]$RouterBaseUrl,
  [string]$OrchestratorBaseUrl,
  [string]$CoordinationBaseUrl,
  [string]$TeamsBotBaseUrl,
  [string]$VectorShard1Url,
  [string]$VectorShard2Url,
  [string]$VectorCollection,
  [string]$PackName,
  [string]$TeacherMinioBase,
  [string]$AdminTenantId,
  [string]$AdminUsername,
  [string]$AdminPassword,
  [string]$ReportPath
)

$ErrorActionPreference = "Stop"

function Resolve-RepoPath {
  param([string]$PathValue, [string]$RepoRoot)
  if (-not $PathValue) { return $null }
  if ([IO.Path]::IsPathRooted($PathValue)) { return $PathValue }
  return (Join-Path $RepoRoot $PathValue)
}

function Read-EnvFile {
  param([string]$PathValue)
  $result = @{}
  if (-not $PathValue) { return $result }
  if (-not (Test-Path $PathValue)) { return $result }
  Get-Content -LiteralPath $PathValue | ForEach-Object {
    $line = $_.Trim()
    if (-not $line) { return }
    if ($line.StartsWith("#")) { return }
    $idx = $line.IndexOf("=")
    if ($idx -lt 1) { return }
    $key = $line.Substring(0, $idx).Trim()
    $val = $line.Substring($idx + 1).Trim()
    if ($key) { $result[$key] = $val }
  }
  return $result
}

function Coalesce {
  param([object]$Value, [object]$Fallback)
  if ($null -eq $Value) { return $Fallback }
  if ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value)) { return $Fallback }
  return $Value
}

function Normalize-BaseUrl {
  param([string]$Value, [string]$Scheme = "http")
  if (-not $Value) { return $null }
  $trim = $Value.Trim().TrimEnd("/")
  if ($trim -match "^https?://") { return $trim }
  return "${Scheme}://$trim"
}

function Build-BaseUrl {
  param([string]$HostName, [int]$Port, [string]$Scheme)
  if (-not $HostName) { return $null }
  if ($Port -le 0) { return "${Scheme}://$HostName" }
  if (($Scheme -eq "http" -and $Port -eq 80) -or ($Scheme -eq "https" -and $Port -eq 443)) {
    return "${Scheme}://$HostName"
  }
  return "${Scheme}://$HostName`:$Port"
}

function Get-HostByRole {
  param($Inventory, [string]$RoleName)
  if (-not $Inventory) { return $null }
  foreach ($hostEntry in $Inventory.hosts) {
    foreach ($role in $hostEntry.roles) {
      $name = if ($role -is [string]) { $role } else { $role.name }
      if ($name -eq $RoleName) {
        return $hostEntry.host
      }
    }
  }
  return $null
}

function Get-HostByName {
  param($Inventory, [string]$HostName)
  if (-not $Inventory) { return $null }
  foreach ($hostEntry in $Inventory.hosts) {
    if ($hostEntry.name -eq $HostName) { return $hostEntry.host }
  }
  return $null
}

function Invoke-Step {
  param([string]$Name, [scriptblock]$Action)
  Write-Host ""
  Write-Host "== $Name =="
  try {
    & $Action
    $script:Results += [pscustomobject]@{ step = $Name; status = "PASS"; error = $null }
  } catch {
    $script:Results += [pscustomobject]@{ step = $Name; status = "FAIL"; error = $_.Exception.Message }
    $script:HadFailure = $true
    Write-Warning "$Name failed: $($_.Exception.Message)"
  }
}

function Invoke-Checked {
  param(
    [scriptblock]$Action,
    [string]$Context
  )

  & $Action
  if ($LASTEXITCODE -ne $null -and $LASTEXITCODE -ne 0) {
    if (-not $Context) { $Context = "Command" }
    throw "$Context failed with exit code $LASTEXITCODE"
  }
}

function Assert-HttpStatus {
  param([string]$Url, [int]$Expected = 200)
  $response = $null
  try {
    $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -Method Get -TimeoutSec 15
    $actual = [int]$response.StatusCode
  } catch {
    if ($_.Exception.Response -and $_.Exception.Response.StatusCode) {
      $actual = [int]$_.Exception.Response.StatusCode
    } else {
      throw
    }
  }
  if ($actual -ne $Expected) {
    throw "Expected $Expected for $Url but got $actual"
  }
  Write-Host "OK $Url -> $actual"
}

function Get-PackNameFromIndex {
  param([string]$PacksBase)
  try {
    $resp = Invoke-RestMethod -Uri "$PacksBase/packs/index.json" -Method Get -TimeoutSec 15
    if ($resp -and $resp.packs -and $resp.packs.Count -gt 0) {
      $candidate = $resp.packs[0].pack_id
      if ($candidate) { return $candidate }
    }
  } catch {
    return $null
  }
  return $null
}

function Resolve-UrlFromEnv {
  param(
    [string]$Explicit,
    [hashtable]$Env,
    [string]$HostName,
    [string]$HostPortKey,
    [string]$PortKey,
    [string]$Scheme = "http"
  )
  if ($Explicit) { return Normalize-BaseUrl -Value $Explicit -Scheme $Scheme }
  if ($Env.ContainsKey($HostPortKey) -and $Env[$HostPortKey]) {
    return Normalize-BaseUrl -Value $Env[$HostPortKey] -Scheme $Scheme
  }
  if ($HostName -and $Env.ContainsKey($PortKey) -and $Env[$PortKey]) {
    $port = [int]$Env[$PortKey]
    return Build-BaseUrl -HostName $HostName -Port $port -Scheme $Scheme
  }
  return $null
}

function Invoke-IdentityLogin {
  param([string]$BaseUrl, [string]$TenantId, [string]$Username, [string]$Password)
  if (-not $BaseUrl) { throw "Identity base URL is required for login." }
  if (-not $Username -or -not $Password) { throw "Admin username/password required for login." }
  $payload = @{
    tenant_id = $TenantId
    username = $Username
    password = $Password
  } | ConvertTo-Json
  $resp = Invoke-RestMethod -Uri "$BaseUrl/auth/login" -Method Post -ContentType "application/json" -Body $payload -TimeoutSec 20
  if (-not $resp.access_token) { throw "Identity login did not return access_token." }
  return $resp.access_token
}

function Invoke-RemoteDockerCheck {
  param(
    [string]$Target,
    [string[]]$ExpectedContainers,
    [string]$KeyPath
  )
  if (-not $Target) { throw "Missing SSH target." }
  $args = @()
  if ($KeyPath) { $args += @("-i", $KeyPath) }
  $args += @($Target, "docker ps --format '{{.Names}}'")
  $output = & ssh @args
  if ($LASTEXITCODE -ne 0) { throw "ssh failed for $Target" }
  $running = $output -split "`n" | ForEach-Object { $_.Trim() } | Where-Object { $_ }
  Write-Host "Running containers on ${Target}:"
  $running | ForEach-Object { Write-Host "  $_" }
  if (-not $ExpectedContainers -or $ExpectedContainers.Count -eq 0) { return }
  $missing = @()
  foreach ($expected in $ExpectedContainers) {
    if (-not ($running -contains $expected)) { $missing += $expected }
  }
  if ($missing.Count -gt 0) {
    throw "Missing containers on ${Target}: $($missing -join ', ')"
  }
}

if ($SkipTlsVerification) {
  try {
    [System.Net.ServicePointManager]::ServerCertificateValidationCallback = { $true }
  } catch {
  }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$inventoryPath = Resolve-RepoPath -PathValue $InventoryPath -RepoRoot $repoRoot
$gatewayEnvPath = Resolve-RepoPath -PathValue $GatewayEnvFile -RepoRoot $repoRoot
$coreEnvPath = Resolve-RepoPath -PathValue $CoreEnvFile -RepoRoot $repoRoot
$teacherEnvPath = Resolve-RepoPath -PathValue $TeacherEnvFile -RepoRoot $repoRoot

$inventory = $null
if (Test-Path $inventoryPath) {
  $inventory = (Get-Content -LiteralPath $inventoryPath -Raw) | ConvertFrom-Json
}

$gatewayEnv = Read-EnvFile -PathValue $gatewayEnvPath
$coreEnv = Read-EnvFile -PathValue $coreEnvPath
$teacherEnv = Read-EnvFile -PathValue $teacherEnvPath

$gatewayHost = Get-HostByRole -Inventory $inventory -RoleName "gateway"
if (-not $gatewayHost) { $gatewayHost = Get-HostByName -Inventory $inventory -HostName "public-gateway-01" }
$coreHost = Get-HostByRole -Inventory $inventory -RoleName "mcp-core"
if (-not $coreHost) { $coreHost = Get-HostByName -Inventory $inventory -HostName "internal-mcp-01" }
$teacherHost = Get-HostByRole -Inventory $inventory -RoleName "teacher_node"
if (-not $teacherHost) { $teacherHost = Get-HostByName -Inventory $inventory -HostName "internal-teacher-01" }

$gatewayScheme = if ($UseHttpsGateway) { "https" } else { "http" }
$gatewayPortKey = if ($UseHttpsGateway) { "GATEWAY_HTTPS_PORT" } else { "GATEWAY_HTTP_PORT" }
$gatewayPort = if ($gatewayEnv.ContainsKey($gatewayPortKey)) { [int]$gatewayEnv[$gatewayPortKey] } else { if ($UseHttpsGateway) { 443 } else { 80 } }

$GatewayBaseUrl = Coalesce $GatewayBaseUrl (Build-BaseUrl -HostName $gatewayHost -Port $gatewayPort -Scheme $gatewayScheme)
$IdentityBaseUrl = Resolve-UrlFromEnv -Explicit $IdentityBaseUrl -Env $gatewayEnv -HostName $coreHost -HostPortKey "IDENTITY_HOSTPORT" -PortKey "IDENTITY_HTTP_PORT"
$PackServiceBaseUrl = Resolve-UrlFromEnv -Explicit $PackServiceBaseUrl -Env $gatewayEnv -HostName $coreHost -HostPortKey "PACK_SERVICE_HOSTPORT" -PortKey "PACK_SERVICE_HTTP_PORT"
$RouterBaseUrl = Resolve-UrlFromEnv -Explicit $RouterBaseUrl -Env $gatewayEnv -HostName $coreHost -HostPortKey "ROUTER_HOSTPORT" -PortKey "ROUTER_HOST_PORT"
$OrchestratorBaseUrl = Resolve-UrlFromEnv -Explicit $OrchestratorBaseUrl -Env $gatewayEnv -HostName $coreHost -HostPortKey "ORCHESTRATOR_HOSTPORT" -PortKey "ORCHESTRATOR_HOST_PORT"
$CoordinationBaseUrl = Resolve-UrlFromEnv -Explicit $CoordinationBaseUrl -Env $gatewayEnv -HostName $coreHost -HostPortKey "COORDINATION_HOSTPORT" -PortKey "COORDINATION_HTTP_PORT"
$TeamsBotBaseUrl = Resolve-UrlFromEnv -Explicit $TeamsBotBaseUrl -Env $gatewayEnv -HostName $coreHost -HostPortKey "" -PortKey "TEAMS_BOT_PORT"
if (-not $TeamsBotBaseUrl) {
  $TeamsBotBaseUrl = Resolve-UrlFromEnv -Explicit $null -Env $coreEnv -HostName $coreHost -HostPortKey "" -PortKey "TEAMS_BOT_PORT"
}

$VectorShard1Url = Coalesce $VectorShard1Url $coreEnv["VECTOR_SHARD_1_URL"]
$VectorShard2Url = Coalesce $VectorShard2Url $coreEnv["VECTOR_SHARD_2_URL"]
$VectorCollection = Coalesce $VectorCollection $coreEnv["VECTOR_COLLECTION"]

if (-not $PackName) {
  if ($PackServiceBaseUrl) {
    $PackName = Get-PackNameFromIndex -PacksBase $PackServiceBaseUrl
  }
  if (-not $PackName) { $PackName = "enterprise" }
}

$AdminTenantId = Coalesce $AdminTenantId $coreEnv["IDENTITY_SUPERADMIN_TENANT_ID"]
$AdminUsername = Coalesce $AdminUsername $coreEnv["IDENTITY_SUPERADMIN_USERNAME"]
$AdminPassword = Coalesce $AdminPassword $coreEnv["IDENTITY_SUPERADMIN_PASSWORD"]

if (-not $TeacherMinioBase -and $teacherEnv.ContainsKey("MINIO_ENDPOINT")) {
  $candidate = $teacherEnv["MINIO_ENDPOINT"]
  if ($candidate -and $candidate -match "^https?://") {
    $TeacherMinioBase = Normalize-BaseUrl -Value $candidate
  }
}

$Results = @()
$HadFailure = $false

if ($Deploy) {
  Invoke-Step -Name "deploy" -Action {
    $deployScript = Resolve-RepoPath -PathValue "OPS/cwi-ops/DEPLOY/deploy.ps1" -RepoRoot $repoRoot
    $args = @("-InventoryPath", $inventoryPath)
    if ($DryRun) { $args += "-DryRun" }
    Invoke-Checked -Context "deploy.ps1" -Action { & powershell -File $deployScript @args }
  }
}

if (-not $SkipRemoteChecks -and $inventory) {
  Invoke-Step -Name "remote-container-checks" -Action {
    $expectedByRole = @{
      "cloudflared" = @("public-edge-cloudflared");
      "gateway" = @("public-core-gateway");
      "cwi-gpt-web-ui" = @("public-ui-gpt-web-ui");
      "cwi-identity-server" = @("internal-01-identity-server");
      "connectors" = @("internal-01-integrations-connectors");
      "edge_agent" = @("client-restricted-edge-agent");
      "node_agent_runtime" = @("client-restricted-node-agent-runtime");
      "pack-service" = @("internal-01-core-pack-service");
      "graph_db" = @("cwi-age-db");
      "vector_shard" = @("internal-02-data-vector-shard");
      "teacher_node" = @("internal-02-teacher-node");
      "teacher_minio" = @("cwi-minio");
      "gpu_worker" = @("internal-02-model-worker", "internal-02-model-worker-pro", "internal-02-model-worker-free");
      "mcp-core" = @(
        "internal-01-core-router",
        "internal-01-core-orchestrator",
        "internal-01-core-coordination-layer",
        "internal-01-core-pack-service",
        "internal-01-core-pin-resolver",
        "internal-01-identity-server",
        "internal-01-bot-teams",
        "internal-01-etl-consent-ingest",
        "internal-01-edge-agent",
        "internal-01-node-agent-runtime",
        "internal-02-data-redis",
        "internal-02-orchestrator-postgres",
        "internal-02-identity-postgres",
        "internal-02-coordination-postgres",
        "internal-02-data-graph-db"
      )
    }
    $sshKey = $null
    if ($inventory.defaults -and $inventory.defaults.sshKeyPath) {
      $sshKey = Resolve-RepoPath -PathValue $inventory.defaults.sshKeyPath -RepoRoot $repoRoot
    }
    foreach ($hostEntry in $inventory.hosts) {
      $sshUser = Coalesce $hostEntry.sshUser (Coalesce $inventory.defaults.sshUser "root")
      $target = "$sshUser@$($hostEntry.host)"
      $expected = @()
      foreach ($role in $hostEntry.roles) {
        $roleName = if ($role -is [string]) { $role } else { $role.name }
        if ($expectedByRole.ContainsKey($roleName)) {
          $expected += $expectedByRole[$roleName]
        }
      }
      $expected = $expected | Select-Object -Unique
      Invoke-RemoteDockerCheck -Target $target -ExpectedContainers $expected -KeyPath $sshKey
    }
  }
}

if (-not $SkipFullStackSmoke) {
  Invoke-Step -Name "full-stack-smoke" -Action {
    if (-not $GatewayBaseUrl) { throw "GatewayBaseUrl not resolved." }
    if (-not $IdentityBaseUrl) { throw "IdentityBaseUrl not resolved." }
    if (-not $PackServiceBaseUrl) { throw "PackServiceBaseUrl not resolved." }
    if (-not $TeamsBotBaseUrl) { throw "TeamsBotBaseUrl not resolved." }
    $scriptPath = Resolve-RepoPath -PathValue "OPS/cwi-ops/scripts/full-stack-smoke.ps1" -RepoRoot $repoRoot
    Invoke-Checked -Context "full-stack-smoke.ps1" -Action {
      & powershell -File $scriptPath `
        -GatewayBaseUrl $GatewayBaseUrl `
        -IdentityBaseUrl $IdentityBaseUrl `
        -PackServiceBaseUrl $PackServiceBaseUrl `
        -TeamsBotBaseUrl $TeamsBotBaseUrl `
        -SkipNodeUi
    }
  }
}

if (-not $SkipPackChecks) {
  Invoke-Step -Name "pack-service-checks" -Action {
    if (-not $PackServiceBaseUrl) { throw "PackServiceBaseUrl not resolved." }
    Assert-HttpStatus -Url "$PackServiceBaseUrl/packs/index.json" -Expected 200
    Assert-HttpStatus -Url "$PackServiceBaseUrl/packs/$PackName/pack.json" -Expected 200
    Assert-HttpStatus -Url "$PackServiceBaseUrl/packs/$PackName/anchor.txt" -Expected 200
    Assert-HttpStatus -Url "$PackServiceBaseUrl/artifacts/reports/index.json" -Expected 200
  }
}

if (-not $SkipVectorChecks) {
  Invoke-Step -Name "vector-shard-checks" -Action {
    if (-not $VectorShard1Url -or -not $VectorShard2Url) {
      throw "Vector shard URLs not resolved."
    }
    $scriptPath = Resolve-RepoPath -PathValue "OPS/cwi-ops/scripts/smoke-vector-shard.ps1" -RepoRoot $repoRoot
    if ($VectorCollection) { $env:VECTOR_COLLECTION = $VectorCollection }
    if ($VectorCollection) {
      Invoke-Checked -Context "smoke-vector-shard.ps1 (shard1)" -Action { & powershell -File $scriptPath -BaseUrl $VectorShard1Url -RequireCollection }
      Invoke-Checked -Context "smoke-vector-shard.ps1 (shard2)" -Action { & powershell -File $scriptPath -BaseUrl $VectorShard2Url -RequireCollection }
    } else {
      Invoke-Checked -Context "smoke-vector-shard.ps1 (shard1)" -Action { & powershell -File $scriptPath -BaseUrl $VectorShard1Url }
      Invoke-Checked -Context "smoke-vector-shard.ps1 (shard2)" -Action { & powershell -File $scriptPath -BaseUrl $VectorShard2Url }
    }
  }
}

if (-not $SkipAuth) {
  Invoke-Step -Name "identity-login" -Action {
    $token = Invoke-IdentityLogin -BaseUrl $IdentityBaseUrl -TenantId $AdminTenantId -Username $AdminUsername -Password $AdminPassword
    $env:CWI_AUTH_TOKEN = $token
    Write-Host "OK identity login"
  }
}

if (-not $SkipGoldTests) {
  Invoke-Step -Name "gold-tests-smoke" -Action {
    if (-not $GatewayBaseUrl) { throw "GatewayBaseUrl not resolved." }
    $env:CWI_PACK_NAME = $PackName
    if ($AdminUsername) { $env:IDENTITY_BOOTSTRAP_ADMIN_USERNAME = $AdminUsername }
    if ($AdminPassword) { $env:IDENTITY_BOOTSTRAP_ADMIN_PASSWORD = $AdminPassword }
    if ($AdminTenantId) { $env:IDENTITY_BOOTSTRAP_TENANT_ID = $AdminTenantId }
    $scriptPath = Resolve-RepoPath -PathValue "OPS/cwi-ops/ci/run-gold-tests.ps1" -RepoRoot $repoRoot
    Invoke-Checked -Context "run-gold-tests.ps1" -Action { & powershell -File $scriptPath -BaseUrl $GatewayBaseUrl }
  }
}

if (-not $SkipLedgerVerify) {
  Invoke-Step -Name "ledger-verify" -Action {
    if (-not $OrchestratorBaseUrl) { throw "OrchestratorBaseUrl not resolved." }
    if (-not $env:CWI_AUTH_TOKEN) { throw "CWI_AUTH_TOKEN missing; login required." }
    $env:CWI_ORCHESTRATOR_URL = $OrchestratorBaseUrl
    $scriptPath = Resolve-RepoPath -PathValue "OPS/cwi-ops/scripts/ledger-verify.ps1" -RepoRoot $repoRoot
    Invoke-Checked -Context "ledger-verify.ps1" -Action { & powershell -File $scriptPath }
  }
}

if (-not $SkipPackMirror -and $TeacherMinioBase) {
  Invoke-Step -Name "pack-mirror-smoke" -Action {
    $scriptPath = Resolve-RepoPath -PathValue "OPS/cwi-ops/scripts/pack-mirror-smoke.sh" -RepoRoot $repoRoot
    if (-not (Get-Command "bash" -ErrorAction SilentlyContinue)) {
      throw "bash not found; cannot run pack-mirror-smoke.sh"
    }
    $env:TEACHER_MINIO_BASE = $TeacherMinioBase
    $env:PACK_SERVICE_URL = $PackServiceBaseUrl
    Invoke-Checked -Context "pack-mirror-smoke.sh" -Action { & bash $scriptPath }
  }
}

$timestamp = (Get-Date).ToUniversalTime().ToString("yyyyMMdd-HHmmss")
if (-not $ReportPath) {
  $ReportPath = "artifacts/deploy-e2e-report-$timestamp.json"
}
if ($ReportPath) {
  $reportDir = Split-Path $ReportPath -Parent
  if ($reportDir) { New-Item -ItemType Directory -Force -Path $reportDir | Out-Null }
  $Results | ConvertTo-Json -Depth 4 | Out-File -FilePath $ReportPath -Encoding utf8
  Write-Host ""
  Write-Host "Report written to $ReportPath"
}

Write-Host ""
Write-Host "Summary:"
$Results | Format-Table -AutoSize

if ($HadFailure) {
  exit 2
}

Write-Host ""
Write-Host "All checks passed."
