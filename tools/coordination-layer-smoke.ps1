param(
  [string]$ProjectPath = "src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Api/Cwi.Coordination.Api.csproj",
  [string]$BaseUrl = "http://localhost:18186",
  [int]$WaitSeconds = 30
)

$ErrorActionPreference = "Stop"

function Get-JsonValue {
  param([string]$Path, [string]$Key)
  if (-not (Test-Path $Path)) {
    return ""
  }
  $json = Get-Content $Path -Raw | ConvertFrom-Json
  $parts = $Key -split "\."
  $current = $json
  foreach ($part in $parts) {
    if ($null -eq $current) { return "" }
    $current = $current.$part
  }
  return $current
}

function Invoke-LoggedRequest {
  param(
    [string]$Method,
    [string]$Url,
    [string]$LogPath,
    [hashtable]$Headers = @{},
    [string]$Body = ""
  )

  try {
    if (($Method -ieq "GET") -or [string]::IsNullOrEmpty($Body)) {
      $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -Method $Method -Headers $Headers -TimeoutSec 5
    } else {
      $response = Invoke-WebRequest -Uri $Url -UseBasicParsing -Method $Method -Headers $Headers -ContentType "application/json" -Body $Body -TimeoutSec 5
    }
    $response | Select-Object StatusCode, Content | Out-String | Tee-Object -FilePath $LogPath -Append
  } catch {
    $exception = $_.Exception
    $status = ""
    $content = ""
    if ($exception.Response) {
      try {
        $status = $exception.Response.StatusCode
        $reader = New-Object System.IO.StreamReader($exception.Response.GetResponseStream())
        $content = $reader.ReadToEnd()
      } catch {
      }
    }
    "StatusCode: $status" | Tee-Object -FilePath $LogPath -Append
    if ($content) {
      "Content: $content" | Tee-Object -FilePath $LogPath -Append
    }
    throw
  }
}

$root = Split-Path -Parent $PSScriptRoot
$qaDir = Join-Path $root "artifacts/qa"
New-Item -ItemType Directory -Force -Path $qaDir | Out-Null
$ts = Get-Date -Format "yyyyMMdd-HHmmss"
$runLog = Join-Path $qaDir "iteration-08-coordination-smoke-$ts.txt"
$appLog = Join-Path $qaDir "iteration-08-coordination-app-$ts.txt"
$appErr = Join-Path $qaDir "iteration-08-coordination-app-$ts.err.txt"

$appSettings = "src/backend/services/cwi-coordination-layer/src/Cwi.Coordination.Api/appsettings.json"
$transportRequire = Get-JsonValue -Path $appSettings -Key "Transport.RequireClientCertificate"
$coordinationRequire = Get-JsonValue -Path $appSettings -Key "Coordination.RequireClientCertificate"

"Coordination appsettings Transport.RequireClientCertificate=$transportRequire" | Tee-Object -FilePath $runLog
"Coordination appsettings Coordination.RequireClientCertificate=$coordinationRequire" | Tee-Object -FilePath $runLog -Append
"Starting coordination-layer on $BaseUrl" | Tee-Object -FilePath $runLog -Append

$env:ASPNETCORE_URLS = $BaseUrl
$env:Transport__RequireClientCertificate = "false"
$env:Coordination__RequireClientCertificate = "false"

$server = Start-Process -FilePath dotnet -ArgumentList @("run", "--project", $ProjectPath, "--no-launch-profile", "--urls", $BaseUrl) -PassThru -NoNewWindow -RedirectStandardOutput $appLog -RedirectStandardError $appErr
try {
  $deadline = (Get-Date).AddSeconds($WaitSeconds)
  do {
    try {
      $resp = Invoke-WebRequest -Uri "$BaseUrl/health" -UseBasicParsing -TimeoutSec 3
      if ($resp.StatusCode -eq 200) { break }
    } catch {
    }
    Start-Sleep -Seconds 2
  } while ((Get-Date) -lt $deadline)

  "GET /health" | Tee-Object -FilePath $runLog -Append
  Invoke-LoggedRequest -Method "Get" -Url "$BaseUrl/health" -LogPath $runLog

  "GET /api/ready" | Tee-Object -FilePath $runLog -Append
  Invoke-LoggedRequest -Method "Get" -Url "$BaseUrl/api/ready" -LogPath $runLog

  "GET /metrics" | Tee-Object -FilePath $runLog -Append
  Invoke-LoggedRequest -Method "Get" -Url "$BaseUrl/metrics" -LogPath $runLog

  $headers = @{ "X-Coordination-Protocol" = "1.0" }
  $payload = @{ node_id = "qa-node-1"; cert_fingerprint = "fp-123"; protocol_version = "1.0" } | ConvertTo-Json

  "POST /api/membership/enroll" | Tee-Object -FilePath $runLog -Append
  Invoke-LoggedRequest -Method "Post" -Url "$BaseUrl/api/membership/enroll" -LogPath $runLog -Headers $headers -Body $payload

  "GET /api/membership" | Tee-Object -FilePath $runLog -Append
  Invoke-LoggedRequest -Method "Get" -Url "$BaseUrl/api/membership" -LogPath $runLog -Headers $headers
} finally {
  if ($server -and -not $server.HasExited) {
    Stop-Process -Id $server.Id -Force
  }
}

