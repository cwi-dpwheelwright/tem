param(
  [string]$InventoryPath = "OPS/cwi-ops/DEPLOY/deploy.inventory.sample.json",
  [switch]$DryRun,
  [switch]$SkipCopy,
  [switch]$SkipComposeUp,
  [switch]$BuildOnRemote,
  [switch]$KeepRemoteBuildContext,
  [switch]$KeepRemoteBuildCache
)

$ErrorActionPreference = "Stop"

function Resolve-RepoPath {
  param([string]$PathValue, [string]$RepoRoot)
  if (-not $PathValue) { return $null }


  if ([IO.Path]::IsPathRooted($PathValue)) { return $PathValue }
  return (Join-Path $RepoRoot $PathValue)
}

function Coalesce {
  param([object]$Value, [object]$Fallback)
  if ($null -eq $Value) { return $Fallback }
  if ($Value -is [string] -and [string]::IsNullOrWhiteSpace($Value)) { return $Fallback }
  return $Value
}

function To-StringArray {
  param([object]$Value)

  if ($null -eq $Value) { return @() }
  if ($Value -is [System.Array]) {
    return @($Value | Where-Object { $null -ne $_ } | ForEach-Object { [string]$_ })
  }
  return @([string]$Value)
}

function Join-RemotePath {
  param([string]$BasePath, [string]$ChildPath)
  if (-not $ChildPath) { return $BasePath }
  if ($ChildPath.StartsWith("/")) { return $ChildPath }
  return ($BasePath.TrimEnd("/") + "/" + $ChildPath.TrimStart("/"))
}

function Invoke-RemoteCommandList {
  param(
    [string]$Target,
    [string]$KeyPath,
    [string]$Password,
    [string]$HostKey,
    [string]$AuthMode,
    [object]$Commands,
    [switch]$DryRun
  )

  foreach ($command in (To-StringArray -Value $Commands)) {
    if ([string]::IsNullOrWhiteSpace($command)) {
      continue
    }

    Invoke-SSH `
      -Target $Target `
      -KeyPath $KeyPath `
      -Password $Password `
      -HostKey $HostKey `
      -AuthMode $AuthMode `
      -DryRun:$DryRun `
      -Command $command
  }
}

function Copy-RolePath {
  param(
    [object]$Entry,
    [string]$RepoRoot,
    [string]$RemotePath,
    [string]$Target,
    [string]$KeyPath,
    [string]$Password,
    [string]$HostKey,
    [string]$AuthMode,
    [switch]$DryRun
  )

  $source = $null
  $targetPath = $null
  if ($Entry -is [string]) {
    $source = [string]$Entry
  } else {
    $source = Coalesce $Entry.source $Entry.path
    $targetPath = Coalesce $Entry.target $Entry.remotePath
  }

  if ([string]::IsNullOrWhiteSpace($source)) {
    throw "copyPaths entries must include a source/path value."
  }

  $sourcePath = Resolve-RepoPath -PathValue $source -RepoRoot $RepoRoot
  if (-not (Test-Path $sourcePath)) {
    throw "copyPaths source not found: $sourcePath"
  }

  $remoteTargetPath = if ($targetPath) {
    Join-RemotePath -BasePath $RemotePath -ChildPath ([string]$targetPath)
  } else {
    $RemotePath
  }

  Invoke-SSH `
    -Target $Target `
    -KeyPath $KeyPath `
    -Password $Password `
    -HostKey $HostKey `
    -AuthMode $AuthMode `
    -DryRun:$DryRun `
    -Command "mkdir -p '$remoteTargetPath'"

  $scpSource = Normalize-ScpPath -PathValue $sourcePath
  $scpTarget = "${Target}:$remoteTargetPath/"
  Invoke-SCP `
    -Source $scpSource `
    -Target $scpTarget `
    -KeyPath $KeyPath `
    -Password $Password `
    -HostKey $HostKey `
    -AuthMode $AuthMode `
    -DryRun:$DryRun
}

function Get-PlinkPath {
  $cmd = Get-Command "plink" -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $candidate = "C:\Program Files\PuTTY\plink.exe"
  if (Test-Path $candidate) { return $candidate }
  $candidate = "C:\Program Files (x86)\PuTTY\plink.exe"
  if (Test-Path $candidate) { return $candidate }
  return $null
}

function Get-PscpPath {
  $cmd = Get-Command "pscp" -ErrorAction SilentlyContinue
  if ($cmd) { return $cmd.Source }
  $candidate = "C:\Program Files\PuTTY\pscp.exe"
  if (Test-Path $candidate) { return $candidate }
  $candidate = "C:\Program Files (x86)\PuTTY\pscp.exe"
  if (Test-Path $candidate) { return $candidate }
  return $null
}

function Get-Inventory {
  param([string]$PathValue)
  if (-not (Test-Path $PathValue)) {
    throw "Inventory file not found: $PathValue"
  }
  $raw = Get-Content -LiteralPath $PathValue -Raw
  return $raw | ConvertFrom-Json
}

function Assert-Tool {
  param([string]$Name)
  $cmd = Get-Command $Name -ErrorAction SilentlyContinue
  if (-not $cmd) {
    throw "Required tool '$Name' not found in PATH. Install OpenSSH client or ensure it is available."
  }
}

function Resolve-AuthMode {
  param(
    [string]$Password,
    [string]$HostKey,
    [string]$KeyPath
  )

  if ($KeyPath) {
    return "ssh"
  }

  if ($Password) {
    if (Get-Command "sshpass" -ErrorAction SilentlyContinue) {
      return "sshpass"
    }
    $plinkPath = Get-PlinkPath
    $pscpPath = Get-PscpPath
    if ($plinkPath -and $pscpPath) {
      return "plink"
    }
    return "ssh"
  }

  return "ssh"
}

function Mask-Command {
  param([string[]]$Args)
  $masked = @()
  for ($i = 0; $i -lt $Args.Count; $i++) {
    if ($Args[$i] -in @("-p", "-pw")) {
      $masked += $Args[$i]
      if ($i + 1 -lt $Args.Count) {
        $masked += "******"
        $i++
        continue
      }
    }
    $masked += $Args[$i]
  }
  return $masked -join " "
}

function Normalize-ScpPath {
  param([string]$PathValue)
  return ($PathValue -replace "\\\\", "/")
}

function New-TempDir {
  $path = Join-Path ([System.IO.Path]::GetTempPath()) ("cwi-build-" + [guid]::NewGuid())
  New-Item -ItemType Directory -Path $path | Out-Null
  return $path
}

function Stage-BuildContext {
  param(
    [string[]]$IncludePaths,
    [string]$RepoRoot
  )

  if (-not $IncludePaths -or $IncludePaths.Count -eq 0) {
    throw "buildContextPaths must be provided when buildImages is set."
  }

  $excludeDirs = @(".vs", "bin", "obj", ".git", "node_modules", ".next", "coverage")

  function Copy-FilteredDirectory {
    param(
      [string]$Source,
      [string]$Destination
    )

    $sourceFull = (Get-Item -LiteralPath $Source).FullName
    $sourceLength = $sourceFull.Length

    Get-ChildItem -LiteralPath $sourceFull -Recurse -Force -File | Where-Object {
      $relative = $_.FullName.Substring($sourceLength).TrimStart('\', '/')
      $parts = $relative -split '[\\/]' 
      -not ($parts | Where-Object { $excludeDirs -contains $_ })
    } | ForEach-Object {
      $relative = $_.FullName.Substring($sourceLength).TrimStart('\', '/')
      $destFile = Join-Path $Destination $relative
      $destDir = Split-Path $destFile -Parent
      if ($destDir -and -not (Test-Path $destDir)) {
        New-Item -ItemType Directory -Path $destDir -Force | Out-Null
      }
      Copy-Item -LiteralPath $_.FullName -Destination $destFile -Force
    }
  }

  $stageRoot = New-TempDir
  foreach ($rel in $IncludePaths) {
    $src = Resolve-RepoPath -PathValue $rel -RepoRoot $RepoRoot
    if (-not (Test-Path $src)) {
      throw "Build context path not found: $src"
    }
    $dest = Join-Path $stageRoot $rel
    $destParent = Split-Path $dest -Parent
    if ($destParent -and -not (Test-Path $destParent)) {
      New-Item -ItemType Directory -Path $destParent -Force | Out-Null
    }

    if (Test-Path -LiteralPath $src -PathType Container) {
      Copy-FilteredDirectory -Source $src -Destination $dest
    } else {
      Copy-Item -LiteralPath $src -Destination $dest -Force
    }
  }
  return $stageRoot
}

function Invoke-SSH {
  param(
    [string]$Target,
    [string]$Command,
    [string]$KeyPath,
    [string]$Password,
    [string]$HostKey,
    [string]$AuthMode,
    [switch]$DryRun
  )

  if (-not $AuthMode) { $AuthMode = "ssh" }

  if ($AuthMode -eq "sshpass") {
    $args = @("-p", $Password, "ssh")
    if ($KeyPath) { $args += @("-i", $KeyPath) }
    $args += @($Target, $Command)

    if ($DryRun) {
      Write-Host ("sshpass " + (Mask-Command -Args $args))
      return
    }

    & sshpass @args
  } elseif ($AuthMode -eq "plink") {
    $args = @()
    if ($KeyPath) { $args += @("-i", $KeyPath) }
    if ($Password) { $args += @("-pw", $Password) }
    if ($HostKey) { $args += @("-hostkey", $HostKey) }
    $args += @("-batch", $Target, $Command)

    if ($DryRun) {
      Write-Host ("plink " + (Mask-Command -Args $args))
      return
    }

    $plinkPath = Get-PlinkPath
    if (-not $plinkPath) { throw "plink not found. Install PuTTY or add plink.exe to PATH." }
    & $plinkPath @args
  } else {
    $args = @()
    if ($KeyPath) { $args += @("-i", $KeyPath) }
    $args += @($Target, $Command)

    if ($DryRun) {
      Write-Host "ssh $($args -join ' ')"
      return
    }

    & ssh @args
  }

  if ($LASTEXITCODE -ne 0) {
    throw "ssh failed for $Target"
  }
}

function Invoke-SCP {
  param(
    [string]$Source,
    [string]$Target,
    [string]$KeyPath,
    [string]$Password,
    [string]$HostKey,
    [string]$AuthMode,
    [switch]$DryRun
  )

  if (-not $AuthMode) { $AuthMode = "ssh" }

  if ($AuthMode -eq "sshpass") {
    $args = @("-p", $Password, "scp", "-r")
    if ($KeyPath) { $args += @("-i", $KeyPath) }
    $args += @($Source, $Target)

    if ($DryRun) {
      Write-Host ("sshpass " + (Mask-Command -Args $args))
      return
    }

    & sshpass @args
  } elseif ($AuthMode -eq "plink") {
    $args = @()
    if ($KeyPath) { $args += @("-i", $KeyPath) }
    if ($Password) { $args += @("-pw", $Password) }
    if ($HostKey) { $args += @("-hostkey", $HostKey) }
    $args += @("-batch", "-r", $Source, $Target)

    if ($DryRun) {
      Write-Host ("pscp " + (Mask-Command -Args $args))
      return
    }

    $pscpPath = Get-PscpPath
    if (-not $pscpPath) { throw "pscp not found. Install PuTTY or add pscp.exe to PATH." }
    & $pscpPath @args
  } else {
    $args = @("-r")
    if ($KeyPath) { $args += @("-i", $KeyPath) }
    $args += @($Source, $Target)

    if ($DryRun) {
      Write-Host "scp $($args -join ' ')"
      return
    }

    & scp @args
  }

  if ($LASTEXITCODE -ne 0) {
    throw "scp failed for $Target"
  }
}

function Invoke-RemoteDockerCleanup {
  param(
    [string]$Target,
    [string]$KeyPath,
    [string]$Password,
    [string]$HostKey,
    [string]$AuthMode,
    [string[]]$Exclusions,
    [bool]$ClearContainers,
    [bool]$ClearImages,
    [bool]$ClearVolumes,
    [switch]$DryRun
  )

  $exclusions = @($Exclusions | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)
  if ($exclusions.Count -eq 0) {
    $exclusions = @(
      "*cadvisor*",
      "*node-exporter*",
      "nextcloud*",
      "postgres:16",
      "redis:7"
    )
  }

  $exclusionBlock = ($exclusions -join "`n")
  $clearContainersFlag = if ($ClearContainers) { "true" } else { "false" }
  $clearImagesFlag = if ($ClearImages) { "true" } else { "false" }
  $clearVolumesFlag = if ($ClearVolumes) { "true" } else { "false" }
  $cleanupTemplate = @'
set -e
CLEAR_CONTAINERS=__CLEAR_CONTAINERS__
CLEAR_IMAGES=__CLEAR_IMAGES__
CLEAR_VOLUMES=__CLEAR_VOLUMES__
EXCLUSIONS_FILE=$(mktemp)
cleanup() {
  rm -f "$EXCLUSIONS_FILE"
}
trap cleanup EXIT

cat > "$EXCLUSIONS_FILE" <<'EOF_EXCLUSIONS'
__EXCLUSIONS__
EOF_EXCLUSIONS

is_excluded_name() {
  value="$1"
  while IFS= read -r pattern || [ -n "$pattern" ]; do
    [ -z "$pattern" ] && continue
    case "$value" in
      $pattern) return 0 ;;
    esac
  done < "$EXCLUSIONS_FILE"
  return 1
}

if [ "$CLEAR_CONTAINERS" = "true" ]; then
  docker ps -a --format '{{.Names}}' | while IFS= read -r name; do
    [ -z "$name" ] && continue
    if is_excluded_name "$name"; then
      echo "Skipping excluded container: $name"
      continue
    fi
    echo "Removing container: $name"
    docker rm -f -v "$name" >/dev/null 2>&1 || docker rm -f "$name" >/dev/null 2>&1 || true
  done
fi

if [ "$CLEAR_IMAGES" = "true" ]; then
  docker image ls --format '{{.Repository}}:{{.Tag}}|{{.ID}}' | sort -u | while IFS='|' read -r image_ref image_id; do
    [ -z "$image_id" ] && continue
    if is_excluded_name "$image_ref"; then
      echo "Skipping excluded image: $image_ref"
      continue
    fi
    echo "Removing image: $image_ref ($image_id)"
    docker image rm -f "$image_id" >/dev/null 2>&1 || true
  done
fi

if [ "$CLEAR_VOLUMES" = "true" ]; then
  docker volume ls -q | sort -u | while IFS= read -r volume_name; do
    [ -z "$volume_name" ] && continue
    if is_excluded_name "$volume_name"; then
      echo "Skipping excluded volume: $volume_name"
      continue
    fi
    echo "Removing volume: $volume_name"
    docker volume rm -f "$volume_name" >/dev/null 2>&1 || true
  done
fi
'@

  $cleanupCmd = $cleanupTemplate
  $cleanupCmd = $cleanupCmd.Replace("__CLEAR_CONTAINERS__", $clearContainersFlag)
  $cleanupCmd = $cleanupCmd.Replace("__CLEAR_IMAGES__", $clearImagesFlag)
  $cleanupCmd = $cleanupCmd.Replace("__CLEAR_VOLUMES__", $clearVolumesFlag)
  $cleanupCmd = $cleanupCmd.Replace("__EXCLUSIONS__", $exclusionBlock)

  Invoke-SSH -Target $Target -KeyPath $KeyPath -Password $Password -HostKey $HostKey -AuthMode $AuthMode -DryRun:$DryRun -Command $cleanupCmd
}

function Invoke-RemoteComposeCleanup {
  param(
    [string]$Target,
    [string]$KeyPath,
    [string]$Password,
    [string]$HostKey,
    [string]$AuthMode,
    [string]$RemotePath,
    [string]$ComposeCommand,
    [string]$ComposeFile,
    [bool]$ClearContainers,
    [bool]$ClearImages,
    [bool]$ClearVolumes,
    [switch]$DryRun
  )

  if (-not ($ClearContainers -or $ClearImages -or $ClearVolumes)) {
    return
  }

  $clearContainersFlag = if ($ClearContainers) { "true" } else { "false" }
  $clearImagesFlag = if ($ClearImages) { "true" } else { "false" }
  $clearVolumesFlag = if ($ClearVolumes) { "true" } else { "false" }

  $cleanupCmd = @"
set -e
cd '$RemotePath'
CLEAR_CONTAINERS=$clearContainersFlag
CLEAR_IMAGES=$clearImagesFlag
CLEAR_VOLUMES=$clearVolumesFlag

if [ "\$CLEAR_CONTAINERS" = "true" ] || [ "\$CLEAR_VOLUMES" = "true" ]; then
  DOWN_ARGS="down --remove-orphans"
  if [ "\$CLEAR_VOLUMES" = "true" ]; then
    DOWN_ARGS="\$DOWN_ARGS -v"
  fi
  $ComposeCommand -f '$ComposeFile' --env-file .env \$DOWN_ARGS || true
fi

if [ "\$CLEAR_IMAGES" = "true" ]; then
  $ComposeCommand -f '$ComposeFile' --env-file .env config --images 2>/dev/null | sort -u | while IFS= read -r image_ref; do
    [ -z "\$image_ref" ] && continue
    echo "Removing role image: \$image_ref"
    docker image rm -f "\$image_ref" >/dev/null 2>&1 || true
  done
fi
"@

  Invoke-SSH -Target $Target -KeyPath $KeyPath -Password $Password -HostKey $HostKey -AuthMode $AuthMode -DryRun:$DryRun -Command $cleanupCmd
}

Assert-Tool -Name "ssh"
Assert-Tool -Name "scp"

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot "..\..\.."))
$inventoryPath = Resolve-RepoPath -PathValue $InventoryPath -RepoRoot $repoRoot
$inventory = Get-Inventory -PathValue $inventoryPath

$defaults = $inventory.defaults
$bundleRoot = Resolve-RepoPath -PathValue (Coalesce $defaults.bundleRoot "OPS/cwi-ops/DEPLOY") -RepoRoot $repoRoot
$deployRoot = Coalesce $defaults.deployRoot "/opt/cwi/DEPLOY"
$composeCommand = Coalesce $defaults.composeCommand "docker compose"
$composeFile = Coalesce $defaults.composeFile "docker-compose.yml"
$composeArgs = Coalesce $defaults.composeArgs "up -d"
$seedEnvFromTemplate = $true
if ($null -ne $defaults.seedEnvFromTemplate) {
  $seedEnvFromTemplate = [bool]$defaults.seedEnvFromTemplate
}
$remoteBuildRoot = Coalesce $defaults.remoteBuildRoot "/tmp/cwi-build"
$keepRemoteBuildCacheDefault = $false
if ($null -ne $defaults.keepRemoteBuildCache) {
  $keepRemoteBuildCacheDefault = [bool]$defaults.keepRemoteBuildCache
}

if (-not $inventory.hosts) {
  throw "No hosts defined in inventory."
}

foreach ($deployHost in $inventory.hosts) {
  $hostName = $deployHost.name
  $hostAddress = Coalesce $deployHost.host $deployHost.hostname
  if (-not $hostAddress) {
    throw "Host entry missing 'host' or 'hostname'."
  }

  $sshUser = Coalesce $deployHost.sshUser $defaults.sshUser
  if (-not $sshUser) {
    throw "Missing sshUser for host $hostName ($hostAddress)."
  }

  $sshPassword = Coalesce $deployHost.sshPassword $defaults.sshPassword
  $sshHostKey = Coalesce $deployHost.sshHostKey $defaults.sshHostKey
  $sshKey = Resolve-RepoPath -PathValue (Coalesce $deployHost.sshKeyPath $defaults.sshKeyPath) -RepoRoot $repoRoot
  $authMode = Resolve-AuthMode -Password $sshPassword -KeyPath $sshKey
  $target = "$sshUser@$hostAddress"

  if ($sshPassword -and $authMode -eq "ssh") {
    Write-Host "Password provided for $hostName; SSH will prompt unless sshpass/plink is installed."
  }

  Write-Host "";
  Write-Host "== Deploy host: $hostName ($target) =="

  if (-not $deployHost.roles) {
    Write-Host "No roles defined for $hostName. Skipping."
    continue
  }

  $clearContainersBeforeDeploy = $false
  if ($null -ne $defaults.clearContainersBeforeDeploy) {
    $clearContainersBeforeDeploy = [bool]$defaults.clearContainersBeforeDeploy
  }
  if ($null -ne $deployHost.clearContainersBeforeDeploy) {
    $clearContainersBeforeDeploy = [bool]$deployHost.clearContainersBeforeDeploy
  }

  $clearImagesBeforeDeploy = $false
  if ($null -ne $defaults.clearImagesBeforeDeploy) {
    $clearImagesBeforeDeploy = [bool]$defaults.clearImagesBeforeDeploy
  }
  if ($null -ne $deployHost.clearImagesBeforeDeploy) {
    $clearImagesBeforeDeploy = [bool]$deployHost.clearImagesBeforeDeploy
  }

  $clearVolumesBeforeDeploy = $false
  if ($null -ne $defaults.clearVolumesBeforeDeploy) {
    $clearVolumesBeforeDeploy = [bool]$defaults.clearVolumesBeforeDeploy
  }
  if ($null -ne $deployHost.clearVolumesBeforeDeploy) {
    $clearVolumesBeforeDeploy = [bool]$deployHost.clearVolumesBeforeDeploy
  }

  $deploymentMode = Coalesce $deployHost.deploymentMode $defaults.deploymentMode
  if (-not $deploymentMode) {
    $deploymentMode = "strict"
  }
  $deploymentMode = ([string]$deploymentMode).Trim().ToLowerInvariant()
  if ($deploymentMode -notin @("strict", "safe")) {
    throw "Invalid deploymentMode '$deploymentMode' for host $hostName. Use 'strict' or 'safe'."
  }

  $clearCleanupExclusions = @()
  if ($null -ne $defaults.clearCleanupExclusions) {
    $clearCleanupExclusions = To-StringArray -Value $defaults.clearCleanupExclusions
  } elseif ($null -ne $defaults.clearContainerExclusions) {
    $clearCleanupExclusions = To-StringArray -Value $defaults.clearContainerExclusions
  }
  if ($null -ne $deployHost.clearCleanupExclusions) {
    $clearCleanupExclusions = To-StringArray -Value $deployHost.clearCleanupExclusions
  } elseif ($null -ne $deployHost.clearContainerExclusions) {
    $clearCleanupExclusions = To-StringArray -Value $deployHost.clearContainerExclusions
  }
  if ($clearCleanupExclusions.Count -eq 0) {
    $clearCleanupExclusions = @(
      "*cadvisor*",
      "*node-exporter*",
      "nextcloud*",
      "postgres:16",
      "redis:7"
    )
  }

  if (($clearContainersBeforeDeploy -or $clearImagesBeforeDeploy -or $clearVolumesBeforeDeploy) -and $deploymentMode -eq "strict") {
    Write-Host "Cleaning Docker state before deploy. Exclusions: $($clearCleanupExclusions -join ', ')"
    Write-Host "  Containers=$clearContainersBeforeDeploy Images=$clearImagesBeforeDeploy Volumes=$clearVolumesBeforeDeploy"
    Invoke-RemoteDockerCleanup `
      -Target $target `
      -KeyPath $sshKey `
      -Password $sshPassword `
      -HostKey $sshHostKey `
      -AuthMode $authMode `
      -DryRun:$DryRun `
      -Exclusions $clearCleanupExclusions `
      -ClearContainers $clearContainersBeforeDeploy `
      -ClearImages $clearImagesBeforeDeploy `
      -ClearVolumes $clearVolumesBeforeDeploy
  } elseif ($clearContainersBeforeDeploy -or $clearImagesBeforeDeploy -or $clearVolumesBeforeDeploy) {
    Write-Host "Safe deployment mode selected. Cleanup will be limited to the roles deployed on this host."
    Write-Host "  Containers=$clearContainersBeforeDeploy Images=$clearImagesBeforeDeploy Volumes=$clearVolumesBeforeDeploy"
  }

  foreach ($role in $deployHost.roles) {
    if ($role -is [string]) {
      $role = [pscustomobject]@{ name = $role }
    }

    $roleName = $role.name
    if (-not $roleName) {
      throw "Role entry missing name on host $hostName."
    }

    $roleBundle = $role.bundle
    if ($roleBundle -and -not [string]::IsNullOrWhiteSpace([string]$roleBundle)) {
      $bundlePath = Resolve-RepoPath -PathValue $roleBundle -RepoRoot $repoRoot
    } else {
      $bundlePath = Join-Path $bundleRoot $roleName
    }
    if (-not (Test-Path $bundlePath)) {
      throw "Bundle path not found for role '$roleName': $bundlePath"
    }

    $remotePath = Coalesce $role.deployPath ("$deployRoot/$roleName")
    $remoteEnvPath = "$remotePath/.env"
    $roleImageTar = $role.imageTar
    $roleBuildImages = $role.buildImages
    $roleBuildContextPaths = $role.buildContextPaths
    $roleBuildOnRemote = $BuildOnRemote -or [bool]$role.buildOnRemote -or ($null -ne $roleBuildImages)
    $remoteBuildPath = Coalesce $role.remoteBuildPath ("$remoteBuildRoot/$roleName")
    $keepRemoteBuildCache = $keepRemoteBuildCacheDefault
    if ($null -ne $deployHost.keepRemoteBuildCache) {
      $keepRemoteBuildCache = [bool]$deployHost.keepRemoteBuildCache
    }
    if ($null -ne $role.keepRemoteBuildCache) {
      $keepRemoteBuildCache = [bool]$role.keepRemoteBuildCache
    }
    if ($KeepRemoteBuildCache) {
      $keepRemoteBuildCache = $true
    }

    Write-Host "-- Role: $roleName"
    Write-Host "   Bundle: $bundlePath"
    Write-Host "   Remote: $remotePath"

    Invoke-SSH -Target $target -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun -Command "mkdir -p $remotePath"

    if (-not $SkipCopy) {
      if ($authMode -eq "plink") {
        $scpSource = Join-Path $bundlePath "*"
        $scpTarget = "${target}:$remotePath/"
        Invoke-SCP -Source $scpSource -Target $scpTarget -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun

        $envTemplate = Join-Path $bundlePath ".env.template"
        if (Test-Path $envTemplate) {
          Invoke-SCP -Source $envTemplate -Target $scpTarget -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun
        }
      } else {
        $scpSource = Normalize-ScpPath -PathValue (Join-Path $bundlePath ".")
        $scpTarget = "${target}:$remotePath/"
        Invoke-SCP -Source $scpSource -Target $scpTarget -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun
      }
    }

    $envFile = Coalesce $role.envFile (Coalesce $deployHost.envFile $defaults.envFile)
    if ($envFile) {
      $envPath = Resolve-RepoPath -PathValue $envFile -RepoRoot $repoRoot
      if (-not (Test-Path $envPath)) {
        throw "Env file not found: $envPath"
      }
      $scpSource = Normalize-ScpPath -PathValue $envPath
      $scpTarget = "${target}:$remoteEnvPath"
      Invoke-SCP -Source $scpSource -Target $scpTarget -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun
    } elseif ($seedEnvFromTemplate) {
      $seedCmd = "if [ ! -f '$remoteEnvPath' ] && [ -f '$remotePath/.env.template' ]; then cp '$remotePath/.env.template' '$remoteEnvPath'; fi"
      Invoke-SSH -Target $target -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun -Command $seedCmd
    }

    if (-not $SkipCopy -and $role.copyPaths) {
      foreach ($copyEntry in $role.copyPaths) {
        Copy-RolePath `
          -Entry $copyEntry `
          -RepoRoot $repoRoot `
          -RemotePath $remotePath `
          -Target $target `
          -KeyPath $sshKey `
          -Password $sshPassword `
          -HostKey $sshHostKey `
          -AuthMode $authMode `
          -DryRun:$DryRun
      }
    }

    $roleComposeFile = Coalesce $role.composeFile $composeFile
    $roleComposeArgs = Coalesce $role.composeArgs $composeArgs

    if ($deploymentMode -eq "safe") {
      Invoke-RemoteComposeCleanup `
        -Target $target `
        -KeyPath $sshKey `
        -Password $sshPassword `
        -HostKey $sshHostKey `
        -AuthMode $authMode `
        -RemotePath $remotePath `
        -ComposeCommand $composeCommand `
        -ComposeFile $roleComposeFile `
        -ClearContainers $clearContainersBeforeDeploy `
        -ClearImages $clearImagesBeforeDeploy `
        -ClearVolumes $clearVolumesBeforeDeploy `
        -DryRun:$DryRun
    }

    if ($roleImageTar) {
      $imageTarPath = Resolve-RepoPath -PathValue $roleImageTar -RepoRoot $repoRoot
      if (-not (Test-Path $imageTarPath)) {
        throw "Image tar not found: $imageTarPath"
      }
      $tarName = [IO.Path]::GetFileName($imageTarPath)
      $scpSource = Normalize-ScpPath -PathValue $imageTarPath
      $scpTarget = "${target}:$remotePath/$tarName"
      Invoke-SCP -Source $scpSource -Target $scpTarget -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun
      $loadCmd = "cd $remotePath && docker load -i $tarName"
      Invoke-SSH -Target $target -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun -Command $loadCmd
    }

    if ($roleBuildOnRemote -and $roleBuildImages) {
      $stageRoot = Stage-BuildContext -IncludePaths $roleBuildContextPaths -RepoRoot $repoRoot
      $remoteBuildPathCreated = $false
      try {
        Invoke-SSH -Target $target -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun -Command "rm -rf $remoteBuildPath && mkdir -p $remoteBuildPath"
        $remoteBuildPathCreated = $true
        $scpSource = Normalize-ScpPath -PathValue (Join-Path $stageRoot ".")
        $scpTarget = "${target}:$remoteBuildPath/"
        Invoke-SCP -Source $scpSource -Target $scpTarget -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun

        $buildCmdLines = @(
          "set -e",
          "cleanup_remote_build() {",
          "  status=`$?",
          "  trap - EXIT"
        )
        if (-not $KeepRemoteBuildContext) {
          $buildCmdLines += "  rm -rf $remoteBuildPath || true"
        }
        if (-not $keepRemoteBuildCache) {
          $buildCmdLines += "  docker builder prune -af >/dev/null || true"
        }
        $buildCmdLines += @(
          "  exit `$status",
          "}",
          "trap cleanup_remote_build EXIT",
          "cd $remoteBuildPath"
        )
        foreach ($build in $roleBuildImages) {
          $image = $build.image
          $dockerfile = $build.dockerfile
          $context = $build.context
          if (-not $image -or -not $dockerfile) {
            throw "buildImages entries must include 'image' and 'dockerfile'."
          }
          $buildArgs = ""
          if ($build.buildArgs) {
            foreach ($arg in $build.buildArgs.PSObject.Properties) {
              $buildArgs += " --build-arg $($arg.Name)=$($arg.Value)"
            }
          }
          $contextPath = $remoteBuildPath
          if ($context) {
            $contextPath = "$remoteBuildPath/$context"
          }
          $buildCmdLines += "if [ ! -d '$contextPath' ] && [ ! -f '$contextPath' ]; then echo 'Missing build context: $contextPath' >&2; exit 1; fi"
          $buildCmdLines += "docker build -t $image -f $remoteBuildPath/$dockerfile$buildArgs $contextPath"
        }
        $buildCmd = $buildCmdLines -join "`n"
        Invoke-SSH -Target $target -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun -Command $buildCmd
        $remoteBuildPathCreated = $false
      } catch {
        if ($remoteBuildPathCreated -and -not $KeepRemoteBuildContext) {
          try {
            Invoke-SSH -Target $target -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun -Command "rm -rf $remoteBuildPath"
          } catch {
            Write-Warning "Failed to remove remote build context after deploy failure: $remoteBuildPath"
          }
        }
        throw
      } finally {
        Remove-Item -Recurse -Force $stageRoot -ErrorAction SilentlyContinue
      }
    }

    Invoke-RemoteCommandList `
      -Target $target `
      -KeyPath $sshKey `
      -Password $sshPassword `
      -HostKey $sshHostKey `
      -AuthMode $authMode `
      -DryRun:$DryRun `
      -Commands $role.preComposeCommands

    if (-not $SkipComposeUp) {
      $composeCmd = "cd $remotePath && $composeCommand -f $roleComposeFile --env-file .env $roleComposeArgs"
      Invoke-SSH -Target $target -KeyPath $sshKey -Password $sshPassword -HostKey $sshHostKey -AuthMode $authMode -DryRun:$DryRun -Command $composeCmd
    }

    Invoke-RemoteCommandList `
      -Target $target `
      -KeyPath $sshKey `
      -Password $sshPassword `
      -HostKey $sshHostKey `
      -AuthMode $authMode `
      -DryRun:$DryRun `
      -Commands $role.postComposeCommands
  }
}

Write-Host "";
Write-Host "Deployment script complete."
