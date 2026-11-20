param(
  [string]$Registry = "local",
  [string]$RouterTag = "10h",
  [string]$OrchestratorTag = "10h",
  [string]$Platforms = "linux/amd64,linux/arm64",
  [switch]$Push
)

$ErrorActionPreference = "Stop"

function BuildImage($name, $dockerfilePath, $contextPath, $tag) {
  $image = "$Registry/$name:$tag"
  $pushArg = $Push.IsPresent ? "--push" : "--load"
  Write-Host "Building $image for $Platforms"
  docker buildx build --platform $Platforms -f $dockerfilePath -t $image $pushArg $contextPath
}

BuildImage "cwi-router" "src/backend/services/cwi-router/Dockerfile" "src/backend/services/cwi-router" $RouterTag
BuildImage "cwi-orchestrator" "src/backend/services/cwi-orchestrator/Dockerfile" "src/backend/services/cwi-orchestrator" $OrchestratorTag
