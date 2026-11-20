param(
    [Parameter(Mandatory = $true)]
    [string]$EnvFile
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $EnvFile)) {
    throw "Env file not found: $EnvFile"
}

$map = @{}
foreach ($line in Get-Content $EnvFile) {
    if ($line -match '^\s*#' -or $line -notmatch '=') { continue }
    $parts = $line.Split('=', 2)
    $map[$parts[0].Trim()] = $parts[1].Trim()
}

function Require-Value {
    param([string]$Key, [string]$Expected)
    $actual = $map[$Key]
    if ($actual -ne $Expected) {
        throw "Architecture check failed: $Key='$actual' expected '$Expected'"
    }
}

function Require-Exists {
    param([string]$Key)
    if (-not $map.ContainsKey($Key) -or [string]::IsNullOrWhiteSpace($map[$Key])) {
        throw "Architecture check failed: missing required env key '$Key'"
    }
}

Require-Exists -Key "TEACHER_NODE_ARTIFACT_BACKEND"
Require-Exists -Key "TEACHER_NODE_PACKS_HOST_PATH"
Require-Value -Key "TEACHER_NODE_PACK_ROOT" -Expected "/app/packs"
Require-Value -Key "TEACHER_NODE_GRAPH_STAGING_ROOT" -Expected "/data/teacher-node/graph-staging"
Require-Value -Key "TEACHER_NODE_VECTOR_ROOT" -Expected "/data/teacher-node/vector"
Require-Value -Key "TEACHER_NODE_LAST_SUCCESSFUL_VALIDATION_PATH" -Expected "/data/teacher-node/last-successful-validation.json"
Require-Value -Key "TEACHER_NODE_CRITIC_REPORT_PATH" -Expected "/data/teacher-node/critic/latest.json"
$backend = $map["TEACHER_NODE_ARTIFACT_BACKEND"].ToLowerInvariant()

switch ($backend) {
    "filesystem" {
        Require-Value -Key "TEACHER_NODE_ARTIFACT_ROOT" -Expected "/data/teacher-node"
        Require-Exists -Key "TEACHER_NODE_ARTIFACT_HOST_PATH"
        Write-Host "OK: teacher-node architecture policy validated for the filesystem-backed internal profile."
        break
    }
    "minio" {
        Require-Exists -Key "MINIO_ENDPOINT"
        Require-Exists -Key "MINIO_ACCESS_KEY"
        Require-Exists -Key "MINIO_SECRET_KEY"
        Require-Exists -Key "MINIO_BUCKET"
        Write-Host "OK: teacher-node architecture policy validated for the MinIO-backed internal profile."
        break
    }
    default {
        throw "Architecture check failed: unsupported TEACHER_NODE_ARTIFACT_BACKEND '$backend'"
    }
}
