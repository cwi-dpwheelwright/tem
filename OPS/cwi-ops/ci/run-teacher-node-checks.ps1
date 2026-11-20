param(
  [string]$PacksRoot = "OPS/cwi-ops/compose/packs",
  [string]$SchemaPath = "contracts/json-schema/pack_schema.json"
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $PacksRoot)) {
  Write-Host "Packs root not found: $PacksRoot"
  exit 1
}

if (-not (Test-Path $SchemaPath)) {
  Write-Host "Pack schema not found: $SchemaPath"
  exit 1
}

Write-Host "Installing teacher-node requirements..."
python -m pip install -r tools/teacher-node/requirements.txt
python -m pip install src/etl

Write-Host "Running teacher-node and graph-tooling tests..."
python -m pytest `
  tools/teacher-node/tests `
  src/etl/tests/test_graph_tooling_ontology.py `
  src/etl/tests/test_graph_tooling_extraction.py `
  src/etl/tests/test_graph_tooling_rollup.py `
  src/etl/tests/test_graph_tooling_writer.py `
  src/etl/tests/test_graph_tooling_backfill.py `
  src/etl/tests/test_graph_tooling_pipeline.py

$packDirs = Get-ChildItem -Path $PacksRoot -Directory
foreach ($packDir in $packDirs) {
  $packPath = Join-Path $packDir.FullName "pack.json"
  $anchorPath = Join-Path $packDir.FullName "anchor.txt"
  if (-not (Test-Path $packPath)) {
    continue
  }
  if (-not (Test-Path $anchorPath)) {
    Write-Host "Missing anchor.txt for $packPath"
    exit 1
  }

  Write-Host "Validating $packPath"
  python tools/teacher-node/teacher_node.py validate --pack $packPath --schema $SchemaPath

  $tmpAnchor = New-TemporaryFile
  python tools/teacher-node/teacher_node.py anchor --pack $packPath --out $tmpAnchor.FullName | Out-Null
  $expected = (Get-Content $anchorPath -Raw).Trim()
  $actual = (Get-Content $tmpAnchor.FullName -Raw).Trim()
  if ($expected -ne $actual) {
    Write-Host "Anchor mismatch for $packPath"
    exit 1
  }
}
