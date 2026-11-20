param(
  [string]$RepoRoot = ".",
  [string[]]$Projects = @("tools/cwi-gold-tests", "src/edge-agent", "src/etl"),
  [string]$ResultsDir = "artifacts/test-results/python"
)

$ErrorActionPreference = "Stop"

$resultsPath = Resolve-Path -Path $ResultsDir -ErrorAction SilentlyContinue
if (-not $resultsPath) {
  New-Item -ItemType Directory -Force -Path $ResultsDir | Out-Null
  $resultsPath = Resolve-Path -Path $ResultsDir
}

function Test-PythonModule {
  param([string]$Module)
  python -c "import $Module" 2>$null
  return $LASTEXITCODE -eq 0
}

foreach ($project in $Projects) {
  $path = Join-Path $RepoRoot $project
  if (-not (Test-Path $path)) {
    Write-Host "Skipping missing $path"
    continue
  }

  $projectName = ($project -replace '[\\/]', '_')
  $junitPath = Join-Path $resultsPath "$projectName.junit.xml"

  Write-Host "Running Python tests for $path"
  $pyprojectPath = Join-Path $path "pyproject.toml"
  $requirementsPath = Join-Path $path "requirements.txt"
  $hasRuffConfig = $false
  $hasMypyConfig = $false
  if (Test-Path $pyprojectPath) {
    $hasRuffConfig = Select-String -Path $pyprojectPath -Pattern "tool.ruff" -SimpleMatch -Quiet
    $hasMypyConfig = Select-String -Path $pyprojectPath -Pattern "tool.mypy" -SimpleMatch -Quiet
  }
  if (Test-Path $pyprojectPath) {
    $depScript = @'
import sys
from pathlib import Path

try:
    import tomllib
except ImportError:  # pragma: no cover - Python <3.11 fallback
    import tomli as tomllib  # type: ignore

data = tomllib.loads(Path(sys.argv[1]).read_text(encoding="utf-8"))
deps = list(data.get("project", {}).get("dependencies", []))
optional = data.get("project", {}).get("optional-dependencies", {})
for key in ("dev", "test", "tests"):
    deps.extend(optional.get(key, []))

seen = set()
ordered = []
for dep in deps:
    if dep not in seen:
        seen.add(dep)
        ordered.append(dep)

print("\n".join(ordered))
'@
    $deps = $depScript | python - $pyprojectPath
    $depList = $deps -split "`r?`n" | Where-Object { $_ -and $_.Trim() }
    if ($depList.Count -gt 0) {
      python -m pip install @depList
    }
  } elseif (Test-Path $requirementsPath) {
    python -m pip install -r $requirementsPath
  }

  $previousPythonPath = $env:PYTHONPATH
  if ($previousPythonPath) {
    $env:PYTHONPATH = "$path;$previousPythonPath"
  } else {
    $env:PYTHONPATH = $path
  }

  if ($hasRuffConfig -and (Test-PythonModule -Module "ruff")) {
    python -m ruff check --config $pyprojectPath $path
  }

  if ($hasMypyConfig -and (Test-PythonModule -Module "mypy")) {
    python -m mypy --config-file $pyprojectPath $path
  }

  if (Test-PythonModule -Module "pytest") {
    $pytestArgs = @($path, "--junitxml", $junitPath)
    $ignoreDirs = @("Lib", "Scripts", ".venv", "venv")
    foreach ($dir in $ignoreDirs) {
      $candidate = Join-Path $path $dir
      if (Test-Path $candidate) {
        $pytestArgs += @("--ignore", $candidate)
      }
    }
    python -m pytest @pytestArgs
  }

  $env:PYTHONPATH = $previousPythonPath
}
