param(
    [string]$GatewayHost = "100.124.140.66",
    [string]$GatewayUser = "root",
    [string]$GatewayRemotePath = "~/cwi-gateway",
    [string]$GatewayEnvFile = "",

    [string]$RouterHost = "",
    [string]$RouterUser = "root",
    [string]$RouterRemotePath = "~/cwi-router",
    [string]$RouterEnvFile = "",

    [string]$OrchestratorHost = "",
    [string]$OrchestratorUser = "root",
    [string]$OrchestratorRemotePath = "~/cwi-orchestrator",
    [string]$OrchestratorEnvFile = "",

    [string]$RedisHost = "",
    [string]$RedisUser = "root",
    [string]$RedisRemotePath = "~/cwi-redis",
    [string]$RedisEnvFile = "",

    [string]$Registry = "local",
    [string]$RouterTag = "10h",
    [string]$CoordinationTag = "10h",
    [string]$OrchestratorTag = "10h",
    [string]$OrchestratorHostPort = "18085",
    [string]$RedisImage = "redis:7.2-alpine",

    [string]$SshKeyPath = "",
    [switch]$BuildOnRemote = $true,
    [string]$RemoteBuildSourcePath = "/tmp/cwi-gateway-router-src",

    [switch]$SkipGateway,
    [switch]$SkipRouter,
    [switch]$SkipOrchestrator,
    [switch]$SkipRedis
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step([string]$Message) {
    Write-Host "==> $Message"
}

function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$File,
        [string[]]$Arguments = @()
    )
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $File $($Arguments -join ' ')"
    }
}

$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..\\..")).Path
$gatewaySource = Join-Path $repoRoot "OPS\\cwi-ops\\compose\\gateway"
$routerSource = Join-Path $repoRoot "OPS\\cwi-ops\\compose\\cwi-router"
$orchestratorSource = Join-Path $repoRoot "OPS\\cwi-ops\\DEPLOY\\cwi-orchestrator"
$routerServiceSource = Join-Path $repoRoot "src\\backend\\services\\cwi-router"
$coordinationServiceSource = Join-Path $repoRoot "src\\backend\\services\\cwi-coordination-layer"
$orchestratorServiceSource = Join-Path $repoRoot "src\\backend\\services\\cwi-orchestrator"
$commonSource = Join-Path $repoRoot "src\\backend\\common"
$contractsSource = Join-Path $repoRoot "contracts\\grpc"
$contractsJsonSchemaSource = Join-Path $repoRoot "contracts\\json-schema"

if (-not (Test-Path $gatewaySource)) { throw "Gateway source folder not found: $gatewaySource" }
if (-not (Test-Path $routerSource)) { throw "Router source folder not found: $routerSource" }
if (-not (Test-Path $orchestratorSource)) { throw "Orchestrator source folder not found: $orchestratorSource" }
if ($BuildOnRemote -and (-not (Test-Path $routerServiceSource))) { throw "Router service source folder not found: $routerServiceSource" }
if ($BuildOnRemote -and (-not (Test-Path $coordinationServiceSource))) { throw "Coordination service source folder not found: $coordinationServiceSource" }
if ($BuildOnRemote -and (-not (Test-Path $orchestratorServiceSource))) { throw "Orchestrator service source folder not found: $orchestratorServiceSource" }
if ($BuildOnRemote -and (-not (Test-Path $commonSource))) { throw "Common source folder not found: $commonSource" }
if ($BuildOnRemote -and (-not (Test-Path $contractsSource))) { throw "Contracts source folder not found: $contractsSource" }
if ($BuildOnRemote -and (-not (Test-Path $contractsJsonSchemaSource))) { throw "Contracts json-schema source folder not found: $contractsJsonSchemaSource" }

$sshKeyArgs = @()
if ($SshKeyPath) {
    if (-not (Test-Path $SshKeyPath)) { throw "SSH key not found: $SshKeyPath" }
    $sshKeyArgs = @("-i", $SshKeyPath)
}

function Invoke-Remote {
    param(
        [Parameter(Mandatory = $true)][string]$User,
        [Parameter(Mandatory = $true)][string]$TargetHost,
        [Parameter(Mandatory = $true)][string]$Command
    )

    $args = @("-o", "StrictHostKeyChecking=accept-new") + $sshKeyArgs + @("$User@$TargetHost", $Command)
    Invoke-External -File "ssh" -Arguments $args
}

function Copy-Remote {
    param(
        [Parameter(Mandatory = $true)][string]$Source,
        [Parameter(Mandatory = $true)][string]$User,
        [Parameter(Mandatory = $true)][string]$TargetHost,
        [Parameter(Mandatory = $true)][string]$Destination
    )

    $args = @("-o", "StrictHostKeyChecking=accept-new")
    if (Test-Path $Source -PathType Container) {
        $args += "-r"
    }
    $args += $sshKeyArgs + @($Source, "${User}@${TargetHost}:$Destination")
    Invoke-External -File "scp" -Arguments $args
}

function New-StagingDir {
    $path = Join-Path ([System.IO.Path]::GetTempPath()) ("cwi-gw-router-" + [guid]::NewGuid())
    New-Item -ItemType Directory -Path $path | Out-Null
    return $path
}

function Stage-Bundle {
    param(
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$StageRoot,
        [string]$EnvOverridePath = ""
    )

    $roleDir = Join-Path $StageRoot $Role
    New-Item -ItemType Directory -Path $roleDir | Out-Null

    Copy-Item (Join-Path $SourceDir "docker-compose.yml") (Join-Path $roleDir "docker-compose.yml") -Force

    if ($Role -eq "gateway") {
        Copy-Item (Join-Path $SourceDir "Caddyfile") (Join-Path $roleDir "Caddyfile") -Force
    }

    if ($EnvOverridePath) {
        if (-not (Test-Path $EnvOverridePath)) { throw "Env override file not found: $EnvOverridePath" }
        Copy-Item $EnvOverridePath (Join-Path $roleDir ".env") -Force
    } else {
        $template = Join-Path $SourceDir ".env.template"
        if (-not (Test-Path $template)) { throw ".env.template not found: $template" }
        Copy-Item $template (Join-Path $roleDir ".env") -Force
    }

    if ($Role -eq "router") {
        $composePath = Join-Path $roleDir "docker-compose.yml"
        $compose = Get-Content $composePath -Raw
        $compose = $compose -replace 'env_file:\s*\.env\.template', 'env_file: .env'
        Set-Content -Path $composePath -Value $compose

        if (-not $EnvOverridePath) {
            $envPath = Join-Path $roleDir ".env"
            $envContent = Get-Content $envPath -Raw
            $envContent = $envContent -replace '(?m)^REGISTRY=.*$', "REGISTRY=$Registry"
            $envContent = $envContent -replace '(?m)^CWI_ROUTER_TAG=.*$', "CWI_ROUTER_TAG=$RouterTag"
            $envContent = $envContent -replace '(?m)^CWI_COORDINATION_TAG=.*$', "CWI_COORDINATION_TAG=$CoordinationTag"
            Set-Content -Path $envPath -Value $envContent
        }
    }

    if ($Role -eq "orchestrator" -and -not $EnvOverridePath) {
        $composePath = Join-Path $roleDir "docker-compose.yml"
        $compose = Get-Content $composePath -Raw
        $compose = $compose -replace '\$\{ORCHESTRATOR_HTTP_PORT\}:8081', '${ORCHESTRATOR_HOST_PORT}:8081'
        Set-Content -Path $composePath -Value $compose

        $envPath = Join-Path $roleDir ".env"
        $envContent = Get-Content $envPath -Raw
        $envContent = $envContent -replace '(?m)^REGISTRY=.*$', "REGISTRY=$Registry"
        $envContent = $envContent -replace '(?m)^CWI_ORCHESTRATOR_TAG=.*$', "CWI_ORCHESTRATOR_TAG=$OrchestratorTag"
        $envContent = $envContent -replace '(?m)^ORCHESTRATOR_HTTP_PORT=.*$', "ORCHESTRATOR_HTTP_PORT=8081"
        if ($envContent -match '(?m)^ORCHESTRATOR_HOST_PORT=') {
            $envContent = $envContent -replace '(?m)^ORCHESTRATOR_HOST_PORT=.*$', "ORCHESTRATOR_HOST_PORT=$OrchestratorHostPort"
        } else {
            $envContent = $envContent.TrimEnd() + "`r`nORCHESTRATOR_HOST_PORT=$OrchestratorHostPort`r`n"
        }
        if ($envContent -notmatch '(?m)^CWI_GRAPH_DB_DSN=') {
            $envContent = $envContent.TrimEnd() + "`r`nCWI_GRAPH_DB_DSN=postgresql://cwi_graph:cwi_graph@cwi-graph-db:5432/cwi_graph`r`n"
        }
        Set-Content -Path $envPath -Value $envContent
    }

    return $roleDir
}

function Stage-RedisBundle {
    param(
        [Parameter(Mandatory = $true)][string]$StageRoot,
        [string]$EnvOverridePath = ""
    )

    $roleDir = Join-Path $StageRoot "redis"
    New-Item -ItemType Directory -Path $roleDir | Out-Null

    $composeContent = @"
version: "3.9"

services:
  redis:
    image: `${REDIS_IMAGE}
    container_name: internal-02-data-redis
    networks:
      - cwi-internal
    restart: unless-stopped

networks:
  cwi-internal:
    name: cwi-internal
"@
    Set-Content -Path (Join-Path $roleDir "docker-compose.yml") -Value $composeContent

    if ($EnvOverridePath) {
        if (-not (Test-Path $EnvOverridePath)) { throw "Env override file not found: $EnvOverridePath" }
        Copy-Item $EnvOverridePath (Join-Path $roleDir ".env") -Force
    } else {
        Set-Content -Path (Join-Path $roleDir ".env") -Value "REDIS_IMAGE=$RedisImage"
    }

    return $roleDir
}

function Stage-RemoteBuildSource {
    param(
        [Parameter(Mandatory = $true)][string]$StageRoot
    )

    $sourceRoot = Join-Path $StageRoot "source"
    $backendRoot = Join-Path $sourceRoot "src\\backend"
    $servicesRoot = Join-Path $backendRoot "services"
    New-Item -ItemType Directory -Path $servicesRoot -Force | Out-Null

    Copy-Item $routerServiceSource (Join-Path $servicesRoot "cwi-router") -Recurse -Force
    Copy-Item $coordinationServiceSource (Join-Path $servicesRoot "cwi-coordination-layer") -Recurse -Force
    Copy-Item $orchestratorServiceSource (Join-Path $servicesRoot "cwi-orchestrator") -Recurse -Force
    Copy-Item $commonSource (Join-Path $backendRoot "common") -Recurse -Force
    Copy-Item $contractsSource (Join-Path $sourceRoot "contracts\\grpc") -Recurse -Force
    Copy-Item $contractsJsonSchemaSource (Join-Path $sourceRoot "contracts\\json-schema") -Recurse -Force

    return $sourceRoot
}

function Deploy-Role {
    param(
        [Parameter(Mandatory = $true)][string]$Role,
        [Parameter(Mandatory = $true)][string]$User,
        [Parameter(Mandatory = $true)][string]$TargetHost,
        [Parameter(Mandatory = $true)][string]$RemotePath,
        [Parameter(Mandatory = $true)][string]$LocalPath,
        [string]$LocalBuildSourcePath = ""
    )

    Write-Step "Deploying $Role to $User@$TargetHost ($RemotePath)"
    Invoke-Remote -User $User -TargetHost $TargetHost -Command "mkdir -p $RemotePath"
    Copy-Remote -Source (Join-Path $LocalPath ".") -User $User -TargetHost $TargetHost -Destination "$RemotePath/"

    $remoteBuildCmd = ""
    if ($Role -eq "router" -and $BuildOnRemote) {
        if ([string]::IsNullOrWhiteSpace($LocalBuildSourcePath)) {
            throw "LocalBuildSourcePath is required for router role when -BuildOnRemote is set."
        }
        Invoke-Remote -User $User -TargetHost $TargetHost -Command "rm -rf $RemoteBuildSourcePath && mkdir -p $RemoteBuildSourcePath"
        Copy-Remote -Source (Join-Path $LocalBuildSourcePath ".") -User $User -TargetHost $TargetHost -Destination "$RemoteBuildSourcePath/"
        $remoteBuildCmd = @"
docker build -t $Registry/cwi-router:$RouterTag -f $RemoteBuildSourcePath/src/backend/services/cwi-router/Dockerfile $RemoteBuildSourcePath
docker build -t $Registry/cwi-coordination-layer:$CoordinationTag -f $RemoteBuildSourcePath/src/backend/services/cwi-coordination-layer/Dockerfile $RemoteBuildSourcePath
rm -rf $RemoteBuildSourcePath
"@
    }
    elseif ($Role -eq "orchestrator" -and $BuildOnRemote) {
        if ([string]::IsNullOrWhiteSpace($LocalBuildSourcePath)) {
            throw "LocalBuildSourcePath is required for orchestrator role when -BuildOnRemote is set."
        }
        Invoke-Remote -User $User -TargetHost $TargetHost -Command "rm -rf $RemoteBuildSourcePath && mkdir -p $RemoteBuildSourcePath"
        Copy-Remote -Source (Join-Path $LocalBuildSourcePath ".") -User $User -TargetHost $TargetHost -Destination "$RemoteBuildSourcePath/"
        $remoteBuildCmd = @"
docker build -t $Registry/cwi-orchestrator:$OrchestratorTag -f $RemoteBuildSourcePath/src/backend/services/cwi-orchestrator/Dockerfile $RemoteBuildSourcePath
rm -rf $RemoteBuildSourcePath
"@
    }

    $remoteCmd = @"
set -e
$remoteBuildCmd
cd $RemotePath
docker compose --env-file .env up -d
docker compose ps
"@
    Invoke-Remote -User $User -TargetHost $TargetHost -Command $remoteCmd
}

$stageRoot = New-StagingDir
try {
    if (-not $SkipOrchestrator -and [string]::IsNullOrWhiteSpace($OrchestratorHost)) {
        if (-not [string]::IsNullOrWhiteSpace($RouterHost)) {
            $OrchestratorHost = $RouterHost
        } else {
            throw "OrchestratorHost is required unless -SkipOrchestrator is set (or RouterHost is provided)."
        }
    }
    if (-not $SkipRedis -and [string]::IsNullOrWhiteSpace($RedisHost)) {
        if (-not [string]::IsNullOrWhiteSpace($RouterHost)) {
            $RedisHost = $RouterHost
        } elseif (-not [string]::IsNullOrWhiteSpace($OrchestratorHost)) {
            $RedisHost = $OrchestratorHost
        } else {
            throw "RedisHost is required unless -SkipRedis is set (or RouterHost/OrchestratorHost is provided)."
        }
    }

    $buildSourceStage = ""
    if ($BuildOnRemote -and (-not $SkipRouter -or -not $SkipOrchestrator)) {
        $buildSourceStage = Stage-RemoteBuildSource -StageRoot $stageRoot
    }

    if (-not $SkipRedis) {
        $redisStage = Stage-RedisBundle -StageRoot $stageRoot -EnvOverridePath $RedisEnvFile
        Deploy-Role -Role "redis" -User $RedisUser -TargetHost $RedisHost -RemotePath $RedisRemotePath -LocalPath $redisStage
    }

    if (-not $SkipOrchestrator) {
        $orchestratorStage = Stage-Bundle -Role "orchestrator" -SourceDir $orchestratorSource -StageRoot $stageRoot -EnvOverridePath $OrchestratorEnvFile
        Deploy-Role -Role "orchestrator" -User $OrchestratorUser -TargetHost $OrchestratorHost -RemotePath $OrchestratorRemotePath -LocalPath $orchestratorStage -LocalBuildSourcePath $buildSourceStage
    }

    if (-not $SkipRouter) {
        if ([string]::IsNullOrWhiteSpace($RouterHost)) {
            throw "RouterHost is required unless -SkipRouter is set."
        }
        $routerStage = Stage-Bundle -Role "router" -SourceDir $routerSource -StageRoot $stageRoot -EnvOverridePath $RouterEnvFile
        Deploy-Role -Role "router" -User $RouterUser -TargetHost $RouterHost -RemotePath $RouterRemotePath -LocalPath $routerStage -LocalBuildSourcePath $buildSourceStage
    }

    if (-not $SkipGateway) {
        $gatewayStage = Stage-Bundle -Role "gateway" -SourceDir $gatewaySource -StageRoot $stageRoot -EnvOverridePath $GatewayEnvFile
        Deploy-Role -Role "gateway" -User $GatewayUser -TargetHost $GatewayHost -RemotePath $GatewayRemotePath -LocalPath $gatewayStage
    }

    Write-Step "Gateway/router/orchestrator/redis deployment complete."
} finally {
    Remove-Item -Recurse -Force $stageRoot -ErrorAction SilentlyContinue
}
