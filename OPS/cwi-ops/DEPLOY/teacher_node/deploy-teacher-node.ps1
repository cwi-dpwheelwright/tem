# Deploy teacher-node to a remote Docker host via SSH/SCP.
param(
    [Parameter(Mandatory = $true)]
    [string]$ServerHost,

    [Parameter(Mandatory = $true)]
    [string]$ServerUser,

    [string]$SshKeyPath,

    [string]$Registry = "local",

    [string]$Tag = "dev",

    # Remote directory where compose + env + image tar will live
    [string]$RemotePath = "",

    # Optional: provide a pre-written env file instead of generating from .env.template
    [string]$EnvFile = "",

    # Build teacher-node image on the remote host instead of locally
    [switch]$BuildOnRemote,

    # Run Teacher Critic inside the deployed container after health passes.
    [switch]$RunCriticValidation
)

$ErrorActionPreference = "Stop"
$ProgressPreference = "SilentlyContinue"

function Write-Step($msg) { Write-Host "==> $msg" }
function Invoke-External {
    param(
        [Parameter(Mandatory = $true)][string]$File,
        [Parameter()][string[]]$Arguments = @()
    )
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed ($LASTEXITCODE): $File $($Arguments -join ' ')"
    }
}
function Test-LocalDocker {
    & docker info | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "Local Docker daemon is not reachable. Start Docker Desktop/Engine and retry."
    }
}

# Resolve paths
$repoRoot = (Resolve-Path (Join-Path $PSScriptRoot "..\\..\\..\\..")).Path
$deployRoot = $PSScriptRoot
$composePath = Join-Path $PSScriptRoot "docker-compose.yml"
$envTemplatePath = Join-Path $PSScriptRoot ".env.template"
$teacherNodeDir = Join-Path $repoRoot "tools\\teacher-node"
$etlDir = Join-Path $repoRoot "src\\etl"
$dockerfilePath = Join-Path $teacherNodeDir "Dockerfile"
$dockerfileBuildPath = "tools/teacher-node/Dockerfile"

if (-not (Test-Path $composePath)) { throw "docker-compose.yml not found at $composePath" }
if (-not (Test-Path $dockerfilePath)) { throw "Dockerfile not found at $dockerfilePath" }
if (-not (Test-Path $etlDir)) { throw "ETL source not found at $etlDir" }

# Construct image name
$imageName = "$Registry/cwi-teacher-node:$Tag"

$roleTeacherContainerName = "internal-02-teacher-node"
$roleArtifactBackend = "filesystem"
$roleArtifactRoot = "/data/teacher-node"
$roleArtifactHostPath = "/srv/cwi/teacher-node-artifacts"
$rolePacksHostPath = "/srv/cwi/packs"
$rolePackRoot = "/app/packs"
$roleGraphStagingRoot = "/data/teacher-node/graph-staging"
$roleVectorRoot = "/data/teacher-node/vector"
$roleValidationMarkerPath = "/data/teacher-node/last-successful-validation.json"
$roleCriticReportPath = "/data/teacher-node/critic/latest.json"
$roleComposeTemplate = $composePath

if (-not $PSBoundParameters.ContainsKey("RemotePath")) {
    $RemotePath = "~/cwi-teacher-node-internal"
}

# Temp staging
$stagingRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("teacher-node-deploy-" + [guid]::NewGuid())
New-Item -ItemType Directory -Path $stagingRoot | Out-Null
$imageTar = Join-Path $stagingRoot "cwi-teacher-node.tar"
$buildContextRoot = Join-Path $stagingRoot "build-context"
$buildToolsRoot = Join-Path $buildContextRoot "tools"
$buildSrcRoot = Join-Path $buildContextRoot "src"
New-Item -ItemType Directory -Path $buildToolsRoot | Out-Null
New-Item -ItemType Directory -Path $buildSrcRoot | Out-Null
Copy-Item $teacherNodeDir (Join-Path $buildToolsRoot "teacher-node") -Recurse -Force
Copy-Item $etlDir (Join-Path $buildSrcRoot "etl") -Recurse -Force

# Prepare compose + env
Copy-Item $roleComposeTemplate (Join-Path $stagingRoot "docker-compose.yml") -Force

$stagingEnv = Join-Path $stagingRoot ".env"
if ($EnvFile) {
    if (-not (Test-Path $EnvFile)) { throw "Provided EnvFile not found: $EnvFile" }
    Copy-Item $EnvFile $stagingEnv -Force
} else {
    if (-not (Test-Path $envTemplatePath)) { throw ".env.template not found at $envTemplatePath" }
    $envContent = Get-Content $envTemplatePath
    $envContent = $envContent -replace '^REGISTRY=.*$', "REGISTRY=$Registry"
    $envContent = $envContent -replace '^CWI_TEACHER_NODE_TAG=.*$', "CWI_TEACHER_NODE_TAG=$Tag"
    $envContent = $envContent -replace '^TEACHER_NODE_CONTAINER_NAME=.*$', "TEACHER_NODE_CONTAINER_NAME=$roleTeacherContainerName"
    $envContent = $envContent -replace '^TEACHER_NODE_ARTIFACT_BACKEND=.*$', "TEACHER_NODE_ARTIFACT_BACKEND=$roleArtifactBackend"
    $envContent = $envContent -replace '^TEACHER_NODE_ARTIFACT_ROOT=.*$', "TEACHER_NODE_ARTIFACT_ROOT=$roleArtifactRoot"
    $envContent = $envContent -replace '^TEACHER_NODE_ARTIFACT_HOST_PATH=.*$', "TEACHER_NODE_ARTIFACT_HOST_PATH=$roleArtifactHostPath"
    $envContent = $envContent -replace '^TEACHER_NODE_PACKS_HOST_PATH=.*$', "TEACHER_NODE_PACKS_HOST_PATH=$rolePacksHostPath"
    $envContent = $envContent -replace '^TEACHER_NODE_PACK_ROOT=.*$', "TEACHER_NODE_PACK_ROOT=$rolePackRoot"
    $envContent = $envContent -replace '^TEACHER_NODE_GRAPH_STAGING_ROOT=.*$', "TEACHER_NODE_GRAPH_STAGING_ROOT=$roleGraphStagingRoot"
    $envContent = $envContent -replace '^TEACHER_NODE_VECTOR_ROOT=.*$', "TEACHER_NODE_VECTOR_ROOT=$roleVectorRoot"
    $envContent = $envContent -replace '^TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH=.*$', "TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH=$roleValidationMarkerPath"
    $envContent = $envContent -replace '^TEACHER_NODE_CRITIC_REPORT_PATH=.*$', "TEACHER_NODE_CRITIC_REPORT_PATH=$roleCriticReportPath"
    Set-Content -Path $stagingEnv -Value $envContent
}

$validator = Join-Path $PSScriptRoot "validate-teacher-node-architecture.ps1"
if (Test-Path $validator) {
    & $validator -EnvFile $stagingEnv
    if (-not $?) {
        throw "Architecture validation failed for teacher-node."
    }
}

function Get-StagedEnvValue {
    param(
        [Parameter(Mandatory = $true)][string]$Key,
        [Parameter(Mandatory = $true)][string]$Default
    )
    $line = Get-Content $stagingEnv |
        Where-Object { $_ -match "^\s*$([regex]::Escape($Key))=" } |
        Select-Object -First 1
    if (-not $line) {
        return $Default
    }
    $value = ($line -replace "^\s*$([regex]::Escape($Key))=", "").Trim()
    if ([string]::IsNullOrWhiteSpace($value)) {
        return $Default
    }
    return $value
}

$teacherHealthPort = Get-StagedEnvValue -Key "TEACHER_NODE_HTTP_PORT" -Default "8084"
$teacherContainerName = Get-StagedEnvValue -Key "TEACHER_NODE_CONTAINER_NAME" -Default $roleTeacherContainerName
$artifactHostPath = Get-StagedEnvValue -Key "TEACHER_NODE_ARTIFACT_HOST_PATH" -Default $roleArtifactHostPath
$packsHostPath = Get-StagedEnvValue -Key "TEACHER_NODE_PACKS_HOST_PATH" -Default $rolePacksHostPath
$criticPackRoot = Get-StagedEnvValue -Key "TEACHER_NODE_PACK_ROOT" -Default $rolePackRoot
$criticArtifactRoot = Get-StagedEnvValue -Key "TEACHER_NODE_ARTIFACT_ROOT" -Default $roleArtifactRoot
$criticVectorRoot = Get-StagedEnvValue -Key "TEACHER_NODE_VECTOR_ROOT" -Default $roleVectorRoot
$criticReportPath = Get-StagedEnvValue -Key "TEACHER_NODE_CRITIC_REPORT_PATH" -Default $roleCriticReportPath
$validationMarkerPath = Get-StagedEnvValue -Key "TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH" -Default $roleValidationMarkerPath

$stagedNames = (Get-ChildItem $stagingRoot | Select-Object -ExpandProperty Name)
Write-Step "Staged files: $($stagedNames -join ', ')"
Write-Step "ArtifactBackend=$roleArtifactBackend ArtifactRoot=$roleArtifactRoot TeacherContainer=$roleTeacherContainerName RemotePath=$RemotePath"

$sshKeyArg = @()
if ($SshKeyPath) {
    if (-not (Test-Path $SshKeyPath)) { throw "SSH key not found: $SshKeyPath" }
    $sshKeyArg = @("-i", $SshKeyPath)
}

function Invoke-Remote($command) {
    $sshCmd = @("ssh") + $sshKeyArg + @("$ServerUser@$ServerHost", $command)
    Write-Output ("Running remote: " + ($sshCmd -join " "))
    Invoke-External -File $sshCmd[0] -Arguments $sshCmd[1..($sshCmd.Count - 1)]
}

function Copy-Remote($source, $dest) {
    $scpTarget = "${ServerUser}@${ServerHost}:$dest"
    $scpCmd = @("scp")
    if (Test-Path $source -PathType Container) {
        $scpCmd += "-r"
    }
    $scpCmd += $sshKeyArg + @($source, $scpTarget)
    Write-Output ("Copying: " + ($scpCmd -join " "))
    Invoke-External -File $scpCmd[0] -Arguments $scpCmd[1..($scpCmd.Count - 1)]
}

if (-not $BuildOnRemote) {
    Test-LocalDocker
    Write-Step "Building image $imageName"
    Push-Location $buildContextRoot
    Invoke-External -File "docker" -Arguments @("build", "-t", $imageName, "-f", $dockerfileBuildPath, ".")
    Pop-Location

    Write-Step "Saving image to $imageTar"
    Invoke-External -File "docker" -Arguments @("save", "-o", $imageTar, $imageName)
}

Write-Step "Copying artifacts to remote /tmp"
if (-not $BuildOnRemote) {
    Copy-Remote $imageTar "/tmp/cwi-teacher-node.tar"
} else {
    Invoke-Remote "rm -rf /tmp/cwi-teacher-node-src"
    Copy-Remote $buildContextRoot "/tmp/cwi-teacher-node-src"
}
Copy-Remote (Join-Path $stagingRoot "docker-compose.yml") "/tmp/docker-compose.yml"
Copy-Remote $stagingEnv "/tmp/.env"

$remoteScript = if (-not $BuildOnRemote) { @"
set -e
mkdir -p $RemotePath
mkdir -p $artifactHostPath/reports $artifactHostPath/documents $artifactHostPath/reconciliation $artifactHostPath/packs $artifactHostPath/graph-staging $artifactHostPath/vector $artifactHostPath/critic $packsHostPath
mv /tmp/cwi-teacher-node.tar $RemotePath/
mv /tmp/docker-compose.yml $RemotePath/
mv /tmp/.env $RemotePath/
cd $RemotePath
docker load -i cwi-teacher-node.tar
if docker compose version >/dev/null 2>&1; then
  docker compose --env-file .env up -d
else
  docker-compose --env-file .env up -d
fi
"@
} else { @"
set -e
mkdir -p $RemotePath
mkdir -p $artifactHostPath/reports $artifactHostPath/documents $artifactHostPath/reconciliation $artifactHostPath/packs $artifactHostPath/graph-staging $artifactHostPath/vector $artifactHostPath/critic $packsHostPath
mv /tmp/docker-compose.yml $RemotePath/
mv /tmp/.env $RemotePath/
docker build -t $imageName -f /tmp/cwi-teacher-node-src/tools/teacher-node/Dockerfile /tmp/cwi-teacher-node-src
cd $RemotePath
if docker compose version >/dev/null 2>&1; then
  docker compose --env-file .env up -d
else
  docker-compose --env-file .env up -d
fi
"@
}

Write-Step "Executing remote compose up"
Invoke-Remote $remoteScript

Write-Step "Validating remote teacher-node health"
Invoke-Remote "curl -fsS --max-time 10 http://127.0.0.1:$teacherHealthPort/health >/dev/null"

if ($RunCriticValidation) {
    Write-Step "Running Teacher Critic validation"
    Invoke-Remote "docker exec $teacherContainerName python teacher_node.py critic-run --pack-root $criticPackRoot --artifact-root $criticArtifactRoot --vector-root $criticVectorRoot --out $criticReportPath --validation-marker $validationMarkerPath >/tmp/cwi-teacher-critic-output.json"
}

Write-Step "Deployment complete. Remote path: $RemotePath"

# Cleanup local staging
Remove-Item -Recurse -Force $stagingRoot
