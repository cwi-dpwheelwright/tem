param(
  [string]$InventoryPath = "OPS/cwi-ops/DEPLOY/deploy.inventory.local.json",
  [string]$GatewayEnvFile = "OPS/cwi-ops/DEPLOY/public-gateway.env",
  [string]$CoreEnvFile = "OPS/cwi-ops/DEPLOY/mcp-core/.env",
  [string]$TeacherEnvFile = "OPS/cwi-ops/DEPLOY/teacher_node/.env",
  [string]$GateName = "deploy-verify",
  [string]$OutputRoot = "artifacts/gates",
  [switch]$SkipDeploy,
  [switch]$SkipVerify,
  [switch]$SkipE2eGateway,
  [bool]$StopOnFailure = $true,
  [switch]$DryRun,
  [switch]$SkipCopy,
  [switch]$SkipComposeUp,
  [switch]$BuildOnRemote,
  [switch]$KeepRemoteBuildContext,
  [switch]$UseHttpsGateway,
  [switch]$SkipTlsVerification,
  [switch]$SkipRemoteChecks,
  [switch]$SkipFullStackSmoke,
  [switch]$SkipPackChecks,
  [switch]$SkipVectorChecks,
  [switch]$SkipGoldTests,
  [switch]$SkipLedgerVerify,
  [switch]$SkipAuth,
  [switch]$SkipPackMirror,
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
  [string]$TenantId = "tenant-1",
  [string]$UserId = "e2e-test",
  [string]$PackId = "",
  [int]$E2eTimeoutSec = 20
)

$ErrorActionPreference = "Stop"

. (Join-Path $PSScriptRoot "gate-framework.ps1")

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

function Resolve-GatewayBaseUrl {
  param(
    [string]$Explicit,
    $Inventory,
    [hashtable]$GatewayEnv,
    [switch]$UseHttpsGateway
  )

  if ($Explicit) { return Normalize-BaseUrl -Value $Explicit -Scheme "http" }
  if ($GatewayEnv.ContainsKey("GATEWAY_BASE_URL") -and $GatewayEnv["GATEWAY_BASE_URL"]) {
    return Normalize-BaseUrl -Value $GatewayEnv["GATEWAY_BASE_URL"] -Scheme "http"
  }

  $gatewayHost = Get-HostByRole -Inventory $Inventory -RoleName "gateway"
  if (-not $gatewayHost) { $gatewayHost = Get-HostByName -Inventory $Inventory -HostName "public-gateway-01" }
  if (-not $gatewayHost) { return $null }

  $scheme = if ($UseHttpsGateway) { "https" } else { "http" }
  $portKey = if ($UseHttpsGateway) { "GATEWAY_HTTPS_PORT" } else { "GATEWAY_HTTP_PORT" }
  $port = if ($GatewayEnv.ContainsKey($portKey)) { [int]$GatewayEnv[$portKey] } else { if ($UseHttpsGateway) { 443 } else { 80 } }

  return Build-BaseUrl -HostName $gatewayHost -Port $port -Scheme $scheme
}

function Add-OptionalArg {
  param([ref]$Args, [string]$Name, [object]$Value)
  if ($null -eq $Value) { return }
  if ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value)) { return }
  $Args.Value += @($Name, $Value)
}

function Add-OptionalSwitch {
  param([ref]$Args, [string]$Name, [switch]$Enabled)
  if ($Enabled) { $Args.Value += $Name }
}

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$inventoryPathResolved = Resolve-RepoPath -PathValue $InventoryPath -RepoRoot $repoRoot
$gatewayEnvPath = Resolve-RepoPath -PathValue $GatewayEnvFile -RepoRoot $repoRoot
$coreEnvPath = Resolve-RepoPath -PathValue $CoreEnvFile -RepoRoot $repoRoot
$teacherEnvPath = Resolve-RepoPath -PathValue $TeacherEnvFile -RepoRoot $repoRoot

$inventory = $null
if ($inventoryPathResolved -and (Test-Path $inventoryPathResolved)) {
  $inventory = (Get-Content -LiteralPath $inventoryPathResolved -Raw) | ConvertFrom-Json
}

$gatewayEnv = Read-EnvFile -PathValue $gatewayEnvPath
$gatewayBase = Resolve-GatewayBaseUrl -Explicit $GatewayBaseUrl -Inventory $inventory -GatewayEnv $gatewayEnv -UseHttpsGateway:$UseHttpsGateway

$gate = New-GateRun -Name $GateName -OutputRoot $OutputRoot
$stopOnFailure = [bool]$StopOnFailure

Push-Location $repoRoot
try {
  $skipRemaining = $false

  if (-not $SkipDeploy -and -not $skipRemaining) {
    Invoke-GateStep -Gate $gate -Name "deploy" -LogPath (Join-Path $gate.OutputDir "deploy.log") -Action {
      $deployScript = Resolve-RepoPath -PathValue "OPS/cwi-ops/DEPLOY/deploy.ps1" -RepoRoot $repoRoot
      $args = @("-InventoryPath", $inventoryPathResolved)
      Add-OptionalSwitch -Args ([ref]$args) -Name "-DryRun" -Enabled:$DryRun
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipCopy" -Enabled:$SkipCopy
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipComposeUp" -Enabled:$SkipComposeUp
      Add-OptionalSwitch -Args ([ref]$args) -Name "-BuildOnRemote" -Enabled:$BuildOnRemote
      Add-OptionalSwitch -Args ([ref]$args) -Name "-KeepRemoteBuildContext" -Enabled:$KeepRemoteBuildContext
      & powershell -File $deployScript @args
    }

    if ($stopOnFailure -and $gate.HadFailure) { $skipRemaining = $true }
  }

  if (-not $SkipVerify -and -not $skipRemaining) {
    Invoke-GateStep -Gate $gate -Name "verify" -LogPath (Join-Path $gate.OutputDir "verify.log") -Action {
      $verifyScript = Resolve-RepoPath -PathValue "OPS/cwi-ops/scripts/deploy-e2e.ps1" -RepoRoot $repoRoot
      $reportPath = Join-Path $gate.OutputDir "deploy-e2e-report.json"
      $args = @(
        "-InventoryPath", $inventoryPathResolved,
        "-GatewayEnvFile", $gatewayEnvPath,
        "-CoreEnvFile", $coreEnvPath,
        "-TeacherEnvFile", $teacherEnvPath,
        "-ReportPath", $reportPath
      )

      Add-OptionalSwitch -Args ([ref]$args) -Name "-UseHttpsGateway" -Enabled:$UseHttpsGateway
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipTlsVerification" -Enabled:$SkipTlsVerification
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipRemoteChecks" -Enabled:$SkipRemoteChecks
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipFullStackSmoke" -Enabled:$SkipFullStackSmoke
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipPackChecks" -Enabled:$SkipPackChecks
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipVectorChecks" -Enabled:$SkipVectorChecks
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipGoldTests" -Enabled:$SkipGoldTests
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipLedgerVerify" -Enabled:$SkipLedgerVerify
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipAuth" -Enabled:$SkipAuth
      Add-OptionalSwitch -Args ([ref]$args) -Name "-SkipPackMirror" -Enabled:$SkipPackMirror

      Add-OptionalArg -Args ([ref]$args) -Name "-GatewayBaseUrl" -Value $gatewayBase
      Add-OptionalArg -Args ([ref]$args) -Name "-IdentityBaseUrl" -Value $IdentityBaseUrl
      Add-OptionalArg -Args ([ref]$args) -Name "-PackServiceBaseUrl" -Value $PackServiceBaseUrl
      Add-OptionalArg -Args ([ref]$args) -Name "-RouterBaseUrl" -Value $RouterBaseUrl
      Add-OptionalArg -Args ([ref]$args) -Name "-OrchestratorBaseUrl" -Value $OrchestratorBaseUrl
      Add-OptionalArg -Args ([ref]$args) -Name "-CoordinationBaseUrl" -Value $CoordinationBaseUrl
      Add-OptionalArg -Args ([ref]$args) -Name "-TeamsBotBaseUrl" -Value $TeamsBotBaseUrl
      Add-OptionalArg -Args ([ref]$args) -Name "-VectorShard1Url" -Value $VectorShard1Url
      Add-OptionalArg -Args ([ref]$args) -Name "-VectorShard2Url" -Value $VectorShard2Url
      Add-OptionalArg -Args ([ref]$args) -Name "-VectorCollection" -Value $VectorCollection
      Add-OptionalArg -Args ([ref]$args) -Name "-PackName" -Value $PackName
      Add-OptionalArg -Args ([ref]$args) -Name "-TeacherMinioBase" -Value $TeacherMinioBase
      Add-OptionalArg -Args ([ref]$args) -Name "-AdminTenantId" -Value $AdminTenantId
      Add-OptionalArg -Args ([ref]$args) -Name "-AdminUsername" -Value $AdminUsername
      Add-OptionalArg -Args ([ref]$args) -Name "-AdminPassword" -Value $AdminPassword

      & powershell -File $verifyScript @args
    }

    if ($stopOnFailure -and $gate.HadFailure) { $skipRemaining = $true }
  }

  if (-not $SkipE2eGateway -and -not $skipRemaining) {
    Invoke-GateStep -Gate $gate -Name "e2e-gateway" -LogPath (Join-Path $gate.OutputDir "e2e-gateway.log") -Action {
      $e2eScript = Resolve-RepoPath -PathValue "OPS/cwi-ops/scripts/run-e2e-gateway.ps1" -RepoRoot $repoRoot
      $baseUrl = $gatewayBase
      if (-not $baseUrl) { throw "GatewayBaseUrl could not be resolved. Provide -GatewayBaseUrl." }
      $args = @("-BaseUrl", $baseUrl, "-TenantId", $TenantId, "-UserId", $UserId, "-TimeoutSec", $E2eTimeoutSec)
      $resolvedPack = $PackId
      if (-not $resolvedPack -and $PackName) { $resolvedPack = $PackName }
      if ($resolvedPack) { $args += @("-PackId", $resolvedPack) }
      & powershell -File $e2eScript @args
    }
  }
}
finally {
  Pop-Location
}

Write-GateReport -Gate $gate | Out-Null
Write-GateSummary -Gate $gate

if ($gate.HadFailure) {
  exit 2
}

Write-Host ""
Write-Host "All gates passed."
