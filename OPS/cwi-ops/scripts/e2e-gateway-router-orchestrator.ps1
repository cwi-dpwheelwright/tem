param(
  [string]$BaseUrl = "http://localhost:18081",
  [string]$IdentityBaseUrl = "",
  [string]$IdentityTenantId = "",
  [string]$IdentityUsername = "",
  [string]$IdentityPassword = "",
  [string]$TenantId = "tenant-1",
  [string]$UserId = "e2e-test",
  [string]$PackId = "",
  [string]$Token = "",
  [switch]$SkipIdentityLogin,
  [string]$ModelTier = "free",
  [int]$TimeoutSec = 20
)

$ErrorActionPreference = "Stop"

function Write-Section($title) {
  Write-Host "==> $title"
}

function Invoke-JsonRequest {
  param(
    [Parameter(Mandatory=$true)][string]$Method,
    [Parameter(Mandatory=$true)][string]$Url,
    [object]$Body = $null,
    [hashtable]$Headers = $null
  )

  $params = @{
    Method = $Method
    Uri = $Url
    TimeoutSec = $TimeoutSec
  }

  if ((Get-Command Invoke-WebRequest).Parameters.ContainsKey("UseBasicParsing")) {
    $params.UseBasicParsing = $true
  }

  if ($Headers) {
    $params.Headers = $Headers
  }

  if ($Body -ne $null) {
    $params.ContentType = "application/json"
    $params.Body = ($Body | ConvertTo-Json -Depth 10)
  }

  $resp = $null
  $content = $null
  try {
    $resp = Invoke-WebRequest @params
  } catch {
    if ($_.Exception.Response) {
      $resp = $_.Exception.Response
    } else {
      throw
    }
  }

  $raw = $null
  if ($resp -and $resp.Content) {
    $raw = $resp.Content
  } elseif ($resp -and $resp.GetResponseStream) {
    try {
      $reader = New-Object System.IO.StreamReader($resp.GetResponseStream())
      $raw = $reader.ReadToEnd()
    } catch {
      $raw = $null
    }
  }

  if ($raw) {
    try { $content = $raw | ConvertFrom-Json } catch { $content = $raw }
  }

  return [pscustomobject]@{
    Status = if ($resp.StatusCode) { [int]$resp.StatusCode } else { $resp.StatusCode }
    Headers = $resp.Headers
    Content = $content
    Raw = $raw
  }
}

function Require-Token([string]$Value) {
  if ([string]::IsNullOrWhiteSpace($Value)) {
    throw "Bearer token required. Provide -Token or set CWI_E2E_BEARER_TOKEN."
  }
}

function Resolve-IdentityValue([string]$Explicit, [string[]]$EnvKeys) {
  if (-not [string]::IsNullOrWhiteSpace($Explicit)) {
    return $Explicit
  }
  foreach ($key in $EnvKeys) {
    $value = [Environment]::GetEnvironmentVariable($key)
    if (-not [string]::IsNullOrWhiteSpace($value)) {
      return $value
    }
  }
  return ""
}

function Convert-Base64UrlToString([string]$value) {
  if ([string]::IsNullOrWhiteSpace($value)) {
    return $null
  }
  $base64 = $value.Replace('-', '+').Replace('_', '/')
  switch ($base64.Length % 4) {
    2 { $base64 += '==' }
    3 { $base64 += '=' }
  }
  try {
    $bytes = [System.Convert]::FromBase64String($base64)
    return [System.Text.Encoding]::UTF8.GetString($bytes)
  } catch {
    return $null
  }
}

function Get-JwtPayload([string]$token) {
  if (-not $token) { return $null }
  $parts = $token.Split('.')
  if ($parts.Length -lt 2) { return $null }
  $json = Convert-Base64UrlToString $parts[1]
  if (-not $json) { return $null }
  try {
    return $json | ConvertFrom-Json
  } catch {
    return $null
  }
}

function Resolve-TenantIdFromToken([string]$token) {
  $payload = Get-JwtPayload $token
  if (-not $payload) { return "" }
  foreach ($key in @("tenant_id", "tenant", "tid", "org", "organization", "tenantId")) {
    $value = $payload.$key
    if ($value -is [string] -and -not [string]::IsNullOrWhiteSpace($value)) {
      return $value
    }
  }
  return ""
}

function Invoke-IdentityLogin {
  param(
    [string]$BaseUrl,
    [string]$TenantId,
    [string]$Username,
    [string]$Password
  )
  if (-not $BaseUrl) { throw "Identity base URL is required for login." }
  if (-not $Username -or -not $Password) { throw "Identity username/password required for login." }
  $payload = @{
    username = $Username
    password = $Password
  }
  if (-not [string]::IsNullOrWhiteSpace($TenantId)) {
    $payload.tenant_id = $TenantId
  }
  $resp = Invoke-RestMethod -Uri "$($BaseUrl.TrimEnd('/'))/auth/login" -Method Post -ContentType "application/json" -Body ($payload | ConvertTo-Json) -TimeoutSec 20
  if (-not $resp.access_token) { throw "Identity login did not return access_token." }
  return $resp.access_token
}

$base = $BaseUrl.TrimEnd("/")

Write-Section "Preflight"
Invoke-JsonRequest -Method "GET" -Url "$base/healthz" | Out-Null
Invoke-JsonRequest -Method "GET" -Url "$base/readyz" | Out-Null
Invoke-JsonRequest -Method "GET" -Url "$base/api/health" | Out-Null
Invoke-JsonRequest -Method "GET" -Url "$base/api/ready" | Out-Null
Write-Host "gateway health/ready OK"

$tokenValue = ""
if (-not $SkipIdentityLogin) {
  $resolvedIdentityBase = Resolve-IdentityValue $IdentityBaseUrl @("CWI_IDENTITY_BASE_URL", "IDENTITY_BASE_URL")
  $resolvedTenantId = Resolve-IdentityValue $IdentityTenantId @("CWI_IDENTITY_TENANT_ID", "IDENTITY_SUPERADMIN_TENANT_ID")
  $resolvedUsername = Resolve-IdentityValue $IdentityUsername @("CWI_IDENTITY_USERNAME", "IDENTITY_SUPERADMIN_USERNAME")
  $resolvedPassword = Resolve-IdentityValue $IdentityPassword @("CWI_IDENTITY_PASSWORD", "IDENTITY_SUPERADMIN_PASSWORD")

  if (-not $resolvedIdentityBase -or -not $resolvedUsername -or -not $resolvedPassword) {
    throw "Identity login required but credentials are missing. Provide -IdentityBaseUrl/-IdentityUsername/-IdentityPassword (and -IdentityTenantId if required), or set CWI_IDENTITY_* env vars. Use -SkipIdentityLogin to use -Token instead."
  }

  Write-Section "Identity login"
  $tokenValue = Invoke-IdentityLogin -BaseUrl $resolvedIdentityBase -TenantId $resolvedTenantId -Username $resolvedUsername -Password $resolvedPassword
} else {
  $tokenValue = if ($Token) { $Token } else { $env:CWI_E2E_BEARER_TOKEN }
  Require-Token $tokenValue
}

$effectiveTenantId = $TenantId
$tenantFromToken = Resolve-TenantIdFromToken $tokenValue
if (-not [string]::IsNullOrWhiteSpace($tenantFromToken) -and $tenantFromToken -ne $effectiveTenantId) {
  $effectiveTenantId = $tenantFromToken
  Write-Host "Using tenant from token: $effectiveTenantId"
}

$authHeaders = @{
  Authorization = "Bearer $tokenValue"
  Accept = "application/json"
  "x-model-tier" = $ModelTier
  "x-policy-mode" = "standard"
}

Write-Section "Pack discovery"
$index = Invoke-JsonRequest -Method "GET" -Url "$base/packs/index.json"
if ($index.Status -ne 200) { throw "Pack index failed: $($index.Status)" }
$packs = if ($index.Content -is [System.Collections.IEnumerable]) { $index.Content } else { $index.Content.packs }
if (-not $packs -or $packs.Count -eq 0) { throw "No packs found at $base/packs/index.json" }
if ([string]::IsNullOrWhiteSpace($PackId)) {
  $PackId = $packs[0].pack_id
}
if ([string]::IsNullOrWhiteSpace($PackId)) { throw "PackId could not be resolved from pack index." }
Write-Host "Using pack_id: $PackId"

$packJson = Invoke-JsonRequest -Method "GET" -Url "$base/packs/$PackId/pack.json"
if ($packJson.Status -ne 200) { throw "pack.json failed: $($packJson.Status)" }
$anchorResp = Invoke-JsonRequest -Method "GET" -Url "$base/packs/$PackId/anchor.txt"
if ($anchorResp.Status -ne 200) { throw "anchor.txt failed: $($anchorResp.Status)" }
$anchor = if ($anchorResp.Raw) { $anchorResp.Raw.Trim() } else { "" }

Write-Section "Ask (gateway -> router -> orchestrator)"
$askPayload = @{
  tenant_id = $effectiveTenantId
  user_id = $UserId
  question = "E2E ask validation"
  pack = @{ name = $PackId; anchor = $anchor }
  context = @{ mode = "standard"; top_k = 5 }
}
$ask = Invoke-JsonRequest -Method "POST" -Url "$base/api/ask" -Body $askPayload -Headers $authHeaders
Write-Host "ask => $($ask.Status)"

if ($ask.Status -ne 200) {
  $detail = if ($ask.Content) { $ask.Content } else { $ask.Raw }
  throw "Ask failed: $($ask.Status). Response: $detail"
}

if (-not $ask.Content -or -not $ask.Content.trace) {
  Write-Host "Ask response payload (missing trace):"
  if ($ask.Content) {
    try { ($ask.Content | ConvertTo-Json -Depth 10) | Write-Host } catch { $ask.Content | Write-Host }
  } elseif ($ask.Raw) {
    $ask.Raw | Write-Host
  }
  throw "Ask response missing trace object."
}

$trace = $ask.Content.trace
if (-not $trace.sources) {
  Write-Host "Ask response payload (missing trace.sources):"
  try { ($ask.Content | ConvertTo-Json -Depth 10) | Write-Host } catch { $ask.Content | Write-Host }
  throw "Ask response missing trace.sources."
}
if (-not $trace.policy_mode) { throw "Ask response missing trace.policy_mode." }
if (-not $trace.model) { throw "Ask response missing trace.model." }
if (-not $trace.pack_hash) { throw "Ask response missing trace.pack_hash." }

$routeId = $ask.Headers["x-route-attestation-id"]
if ($routeId) { Write-Host "route attestation id: $routeId" }
$requestId = $ask.Headers["x-request-id"]
if ($requestId) { Write-Host "request id: $requestId" }

Write-Section "Summary"
Write-Host "gateway.health: OK"
Write-Host "pack.index: OK"
Write-Host "ask: OK"
