param(
  [string]$Registry = "registry.example.com",
  [string]$RouterTag = "10h",
  [string]$OrchestratorTag = "10h",
  [string]$IdentityTag = "10h"
)

$ErrorActionPreference = "Stop"

Write-Host "Building cwi-router..."
docker build -f src/backend/services/cwi-router/Dockerfile -t "$Registry/cwi-router:$RouterTag" .

Write-Host "Building cwi-orchestrator..."
docker build -f src/backend/services/cwi-orchestrator/Dockerfile -t "$Registry/cwi-orchestrator:$OrchestratorTag" .

Write-Host "Building cwi-identity-server..."
docker build -f src/backend/services/cwi-identity-server/Dockerfile -t "$Registry/cwi-identity-server:$IdentityTag" .
