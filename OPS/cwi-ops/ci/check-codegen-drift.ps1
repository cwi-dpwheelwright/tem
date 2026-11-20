param(
  [string]$CodegenRoot = "tools/codegen"
)

$ErrorActionPreference = "Stop"

Push-Location $CodegenRoot
npm install
node src/codegen.js
Pop-Location

$changes = git status --porcelain
if ($changes) {
  Write-Host "Codegen drift detected. Working tree is dirty."
  Write-Host $changes
  exit 1
}
