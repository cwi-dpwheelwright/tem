param(
    [string]$ComposeFile = "OPS/cwi-ops/compose/test/docker-compose.test.yml",
    [string]$Profile = "smoke",
    [string]$EnvFile = "OPS/cwi-ops/compose/test/.env",
    [string]$ConfigPath = "src/edge-agent/configs/edge-agent.example.yaml",
    [string]$MappingPath = "src/edge-agent/configs/telemetry_tag_map.example.yaml",
    [string]$SamplePath = "src/edge-agent/configs/telemetry_sample.csv",
    [string]$BufferPath = "artifacts/edge-agent/buffer.jsonl",
    [string]$GatewayUrl = "",
    [string]$TenantId = ""
)

$ErrorActionPreference = "Stop"

function Get-EnvValue {
    param(
        [string]$Path,
        [string]$Key
    )
    if (-not (Test-Path $Path)) {
        return ""
    }
    $lines = Get-Content $Path
    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if (-not $trimmed -or $trimmed.StartsWith("#")) {
            continue
        }
        $parts = $trimmed.Split("=", 2)
        if ($parts.Length -ne 2) {
            continue
        }
        if ($parts[0].Trim() -eq $Key) {
            return $parts[1].Trim()
        }
    }
    return ""
}

function Ensure-AuthToken {
    param([string]$EnvFilePath)

    if ($env:CWI_EDGE_AGENT_JWT) {
        return $env:CWI_EDGE_AGENT_JWT
    }
    if ($env:CWI_AUTH_TOKEN) {
        $env:CWI_EDGE_AGENT_JWT = $env:CWI_AUTH_TOKEN
        return $env:CWI_EDGE_AGENT_JWT
    }

    $identityPort = Get-EnvValue -Path $EnvFilePath -Key "IDENTITY_HTTP_PORT"
    $identityUrl = Get-EnvValue -Path $EnvFilePath -Key "IDENTITY_URL"
    if (-not $identityUrl -and $identityPort) {
        $identityUrl = "http://localhost:$identityPort"
    }

    $gatewayPort = Get-EnvValue -Path $EnvFilePath -Key "GATEWAY_HTTP_PORT"
    $domain = Get-EnvValue -Path $EnvFilePath -Key "CWI_DOMAIN"
    if (-not $domain) {
        $domain = "localhost"
    }
    if ($identityUrl -match "cwi-identity-server" -and $gatewayPort) {
        $identityUrl = "http://${domain}:$gatewayPort"
    }
    if (-not $identityUrl -and $gatewayPort) {
        $identityUrl = "http://${domain}:$gatewayPort"
    }

    $tenant = Get-EnvValue -Path $EnvFilePath -Key "IDENTITY_BOOTSTRAP_TENANT_ID"
    if (-not $tenant) {
        $tenant = Get-EnvValue -Path $EnvFilePath -Key "CWI_TENANT_ID"
    }
    if (-not $tenant) {
        $tenant = "tenant-1"
    }

    $email = Get-EnvValue -Path $EnvFilePath -Key "IDENTITY_BOOTSTRAP_ADMIN_EMAIL"
    $username = Get-EnvValue -Path $EnvFilePath -Key "IDENTITY_BOOTSTRAP_ADMIN_USERNAME"
    $password = Get-EnvValue -Path $EnvFilePath -Key "IDENTITY_BOOTSTRAP_ADMIN_PASSWORD"

    if (-not $password) {
        throw "IDENTITY_BOOTSTRAP_ADMIN_PASSWORD is required to fetch auth token."
    }
    if (-not $identityUrl) {
        throw "IDENTITY_URL or IDENTITY_HTTP_PORT is required to fetch auth token."
    }

    $payload = @{
        tenant_id = $tenant
        password = $password
    }
    if ($email) {
        $payload.email = $email
    } elseif ($username) {
        $payload.username = $username
    } else {
        throw "IDENTITY_BOOTSTRAP_ADMIN_EMAIL or IDENTITY_BOOTSTRAP_ADMIN_USERNAME is required."
    }

    $resp = Invoke-RestMethod -Method Post -Uri "$identityUrl/auth/login" -ContentType "application/json" -Body ($payload | ConvertTo-Json)
    if (-not $resp.access_token) {
        throw "Identity login did not return access_token."
    }
    $env:CWI_AUTH_TOKEN = $resp.access_token
    $env:CWI_EDGE_AGENT_JWT = $resp.access_token
    return $resp.access_token
}

if (-not $GatewayUrl) {
    $gatewayPort = Get-EnvValue -Path $EnvFile -Key "GATEWAY_HTTP_PORT"
    $domain = Get-EnvValue -Path $EnvFile -Key "CWI_DOMAIN"
    if (-not $domain) {
        $domain = "localhost"
    }
    if ($gatewayPort) {
        $GatewayUrl = "http://${domain}:$gatewayPort"
    }
}
if (-not $GatewayUrl) {
    $GatewayUrl = "http://localhost:18081"
}

if (-not $TenantId) {
    $TenantId = Get-EnvValue -Path $EnvFile -Key "IDENTITY_BOOTSTRAP_TENANT_ID"
}
if (-not $TenantId) {
    $TenantId = "tenant-1"
}

Write-Host "Using gateway_url=$GatewayUrl tenant_id=$TenantId"

$smokeConfig = "artifacts/edge-agent/edge-agent.smoke.yaml"
New-Item -ItemType Directory -Force -Path (Split-Path $smokeConfig) | Out-Null
@"
gateway_url: "$GatewayUrl"
tenant_id: "$TenantId"
poll_interval_seconds: 5
buffer_path: "$BufferPath"
"@ | Set-Content -Path $smokeConfig

Write-Host "Starting stack..."
$previousErrorPreference = $ErrorActionPreference
$ErrorActionPreference = "Continue"
$composeOutput = & docker compose -f $ComposeFile --profile $Profile --env-file $EnvFile up -d 2>&1
$ErrorActionPreference = $previousErrorPreference
$composeOutput | ForEach-Object { Write-Host $_ }
if ($LASTEXITCODE -ne 0) {
    throw "docker compose up failed with exit code $LASTEXITCODE"
}

Write-Host "Fetching auth token..."
for ($attempt = 1; $attempt -le 10; $attempt++) {
    try {
        $token = Ensure-AuthToken -EnvFilePath $EnvFile
        break
    } catch {
        if ($attempt -eq 10) {
            throw
        }
        Start-Sleep -Seconds 3
    }
}

Write-Host "Preparing buffer..."
New-Item -ItemType Directory -Force -Path (Split-Path $BufferPath) | Out-Null

Write-Host "Importing telemetry sample to buffer..."
python -m edge_agent import-file --input $SamplePath --mapping $MappingPath --tenant-id $TenantId --buffer $BufferPath

Write-Host "Sending buffered telemetry..."
$runProcess = Start-Process -FilePath python -ArgumentList @("-m", "edge_agent", "run", "--config", $smokeConfig, "--max-batch", "10") -PassThru -NoNewWindow
Start-Sleep -Seconds 15
if (-not $runProcess.HasExited) {
    Stop-Process -Id $runProcess.Id -Force
}

Write-Host "Capturing artifacts..."
$dest = "artifacts/edge-agent/buffer.jsonl"
if ($BufferPath -ne $dest) {
    Copy-Item $BufferPath $dest -Force
}

Write-Host "Smoke run complete."
