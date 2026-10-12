param(
    [string]$Version,
    [string[]]$ReleaseNotes,
    [string]$MinimumSupportedVersion,
    [switch]$SkipGit,
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

# ---------------------------------------------------------------------------
# PartsPortal one-command release script
#
# Normal:
#   .\Push-Update.ps1
#
# Examples:
#   .\Push-Update.ps1 -Version 3.2.7 -ReleaseNotes @("Fix one","Fix two")
#   .\Push-Update.ps1 -SkipDeploy
#   .\Push-Update.ps1 -SkipGit
# ---------------------------------------------------------------------------

$Repo = $PSScriptRoot
$Vm = "10.130.206.135"
$VmUser = "admin"
$InstallBase = "http://$Vm/partsportal/install"
$VersionApi = "http://$Vm/partsportal/api/system/client-version"

$ClientProject = Join-Path $Repo "src\PartsPortal.Client\PartsPortal.Client.csproj"
$ApiSettings = Join-Path $Repo "src\PartsPortal.Api\appsettings.json"
$SetupProject = Join-Path $Repo "PartsPortal.Setup\PartsPortal.Setup.vdproj"
$Solution = Join-Path $Repo "PartsPortal.sln"
$InstallPage = Join-Path $Repo "deploy\install\index.html"

function Step([string]$Message) {
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Run([string]$Exe, [string[]]$ArgumentList) {
    & $Exe @ArgumentList
    if ($LASTEXITCODE -ne 0) {
        throw "$Exe failed with exit code $LASTEXITCODE."
    }
}

function Current-Version {
    [xml]$x = Get-Content $ClientProject
    return ([string]$x.Project.PropertyGroup.Version).Trim()
}

function Next-Patch([string]$Value) {
    $v = [Version]::Parse($Value)
    return "$($v.Major).$($v.Minor).$($v.Build + 1)"
}

function Validate-Version([string]$Value, [string]$Name) {
    $parsed = $null
    if (-not [Version]::TryParse($Value, [ref]$parsed) -or $Value.Split('.').Count -ne 3) {
        throw "$Name must use three parts, for example 3.2.7."
    }
}

function Html([string]$Value) {
    return [System.Net.WebUtility]::HtmlEncode($Value)
}

function ShellValue([string]$Value) {
    $Value = $Value.Replace("\", "\\")
    $Value = $Value.Replace('"', '\"')
    $Value = $Value.Replace('$', '\$')
    $Value = $Value.Replace('`', '\`')
    return '"' + $Value + '"'
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    # Files consumed by Linux must use LF endings. PowerShell here-strings on
    # Windows otherwise produce CRLF, which breaks bash options such as pipefail.
    $Content = $Content.Replace("`r`n", "`n").Replace("`r", "`n")
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Find-Devenv {
    # 1) If Visual Studio is already on PATH, use it.
    foreach ($name in @("devenv.com", "devenv.exe")) {
        $cmd = Get-Command $name -ErrorAction SilentlyContinue
        if ($cmd -and (Test-Path $cmd.Source)) {
            Write-Host "Using Visual Studio: $($cmd.Source)" -ForegroundColor DarkGray
            return $cmd.Source
        }
    }

    # 2) Ask vswhere. Include prerelease installs because newer Visual Studio
    #    versions may not be returned by a plain -latest query.
    $vswhereCandidates = @(
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Microsoft Visual Studio\Installer\vswhere.exe")
    )

    foreach ($vswhere in $vswhereCandidates | Select-Object -Unique) {
        if (-not (Test-Path $vswhere)) {
            continue
        }

        foreach ($target in @("Common7\IDE\devenv.com", "Common7\IDE\devenv.exe")) {
            $found = & $vswhere -latest -prerelease -products * -find $target 2>$null | Select-Object -First 1
            if ($found -and (Test-Path $found)) {
                Write-Host "Using Visual Studio: $found" -ForegroundColor DarkGray
                return $found
            }
        }

        $installRoots = & $vswhere -all -prerelease -products * -property installationPath 2>$null
        foreach ($installRoot in $installRoots) {
            foreach ($file in @("devenv.com", "devenv.exe")) {
                $candidate = Join-Path $installRoot "Common7\IDE\$file"
                if (Test-Path $candidate) {
                    Write-Host "Using Visual Studio: $candidate" -ForegroundColor DarkGray
                    return $candidate
                }
            }
        }
    }

    # 3) Last-resort direct search. This covers custom/newer Visual Studio
    #    folder layouts that vswhere does not report correctly.
    $searchRoots = @(
        (Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Microsoft Visual Studio"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Microsoft Visual Studio")
    ) | Select-Object -Unique

    foreach ($root in $searchRoots) {
        if (-not (Test-Path $root)) {
            continue
        }

        $found = Get-ChildItem $root -Filter "devenv.com" -File -Recurse -ErrorAction SilentlyContinue |
            Select-Object -First 1

        if (-not $found) {
            $found = Get-ChildItem $root -Filter "devenv.exe" -File -Recurse -ErrorAction SilentlyContinue |
                Select-Object -First 1
        }

        if ($found) {
            Write-Host "Using Visual Studio: $($found.FullName)" -ForegroundColor DarkGray
            return $found.FullName
        }
    }

    throw "Visual Studio IDE was not found. Open Visual Studio Installer and confirm a full Visual Studio installation is present."
}

Set-Location $Repo

# Keep release commits clean: do not sweep unrelated edits into a deployment.
$dirty = & git status --porcelain
if ($LASTEXITCODE -ne 0) {
    throw "This folder is not a Git repository."
}
if ($dirty) {
    Write-Host "Uncommitted changes found:" -ForegroundColor Yellow
    $dirty | ForEach-Object { Write-Host "  $_" }
    throw "Commit or stash those changes before running Push-Update."
}

$current = Current-Version

if (-not $Version) {
    $defaultVersion = Next-Patch $current
    $answer = Read-Host "New PartsPortal version [$defaultVersion]"
    $Version = if ([string]::IsNullOrWhiteSpace($answer)) { $defaultVersion } else { $answer.Trim() }
}
Validate-Version $Version "Version"

$settings = Get-Content $ApiSettings -Raw | ConvertFrom-Json

if (-not $MinimumSupportedVersion) {
    $oldMinimum = [string]$settings.ClientVersion.MinimumSupportedVersion
    $answer = Read-Host "Minimum supported version [$oldMinimum]"
    $MinimumSupportedVersion = if ([string]::IsNullOrWhiteSpace($answer)) { $oldMinimum } else { $answer.Trim() }
}
Validate-Version $MinimumSupportedVersion "MinimumSupportedVersion"

if (-not $ReleaseNotes -or $ReleaseNotes.Count -eq 0) {
    $notes = [System.Collections.Generic.List[string]]::new()

    Write-Host ""
    Write-Host "Enter release notes one at a time. Press Enter on a blank line when finished." -ForegroundColor Yellow

    while ($true) {
        $note = Read-Host "Release note"
        if ([string]::IsNullOrWhiteSpace($note)) {
            break
        }
        $notes.Add($note.Trim())
    }

    if ($notes.Count -eq 0) {
        $notes.Add("PartsPortal $Version release.")
    }

    $ReleaseNotes = $notes.ToArray()
}

$msiName = "PartsPortal-Setup-$Version.msi"
$downloadUrl = "$InstallBase/$msiName"

Write-Host ""
Write-Host "PartsPortal release" -ForegroundColor Green
Write-Host "  Current version : $current"
Write-Host "  New version     : $Version"
Write-Host "  Minimum version : $MinimumSupportedVersion"
Write-Host "  Installer       : $msiName"
Write-Host "  VM              : $Vm"
Write-Host ""

$confirm = Read-Host "Build and push this release? [Y/n]"
if ($confirm -match "^(n|no)$") {
    Write-Host "Cancelled."
    exit 0
}

# Preserve editable files so a failed build/deploy can return the repo to clean.
$originalClient = Get-Content $ClientProject -Raw
$originalApiSettings = Get-Content $ApiSettings -Raw
$originalSetup = Get-Content $SetupProject -Raw
$originalIndex = Get-Content $InstallPage -Raw

$keepLocalReleaseChanges = $false

try {
    Step "Updating client version"

    $text = $originalClient
    $text = [regex]::Replace($text, "<Version>.*?</Version>", "<Version>$Version</Version>", 1)
    $text = [regex]::Replace($text, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$Version</AssemblyVersion>", 1)
    $text = [regex]::Replace($text, "<FileVersion>.*?</FileVersion>", "<FileVersion>$Version</FileVersion>", 1)
    $text = [regex]::Replace($text, "<InformationalVersion>.*?</InformationalVersion>", "<InformationalVersion>$Version (Released)</InformationalVersion>", 1)
    Set-Content $ClientProject $text -Encoding UTF8

    Step "Updating MSI metadata"

    $vd = $originalSetup
    $productCode = ([Guid]::NewGuid().ToString("B")).ToUpper()
    $packageCode = ([Guid]::NewGuid().ToString("B")).ToUpper()

    $vd = [regex]::Replace(
        $vd,
        '"OutputFilename" = "8:Release\\[^"]+\.msi"',
        '"OutputFilename" = "8:Release\\' + $msiName + '"',
        1)

    $vd = [regex]::Replace(
        $vd,
        '"ProductVersion" = "8:[^"]+"',
        '"ProductVersion" = "8:' + $Version + '"',
        1)

    # This matches the setup project's GUID ProductCode, not the prerequisite ProductCode.
    $vd = [regex]::Replace(
        $vd,
        '"ProductCode" = "8:\{[A-Fa-f0-9-]+\}"',
        '"ProductCode" = "8:' + $productCode + '"',
        1)

    $vd = [regex]::Replace(
        $vd,
        '"PackageCode" = "8:\{[A-Fa-f0-9-]+\}"',
        '"PackageCode" = "8:' + $packageCode + '"',
        1)

    $vd = $vd.Replace('"RemovePreviousVersions" = "11:FALSE"', '"RemovePreviousVersions" = "11:TRUE"')
    Set-Content $SetupProject $vd -Encoding UTF8

    Step "Updating release API defaults"

    $settings.ClientVersion.LatestVersion = $Version
    $settings.ClientVersion.MinimumSupportedVersion = $MinimumSupportedVersion
    $settings.ClientVersion.DownloadUrl = $downloadUrl
    $settings.ClientVersion.ReleaseNotes = @($ReleaseNotes)
    $settings | ConvertTo-Json -Depth 10 | Set-Content $ApiSettings -Encoding UTF8

    Step "Generating install page"

    $bullets = ($ReleaseNotes | ForEach-Object {
        "      <li>$(Html $_)</li>"
    }) -join [Environment]::NewLine

    $page = @"
<!doctype html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1">
<title>PartsPortal</title>
<style>
body{font-family:"Segoe UI",Arial,sans-serif;background:#0f172a;color:#e5e7eb;margin:0;min-height:100vh;display:grid;place-items:center}
main{width:min(680px,calc(100% - 40px));background:#111827;border:1px solid #334155;border-radius:18px;padding:32px}
.version{color:#94a3b8;margin-bottom:24px}
a{display:inline-block;padding:12px 18px;border-radius:10px;background:#2563eb;color:white;text-decoration:none;font-weight:600}
li{margin:8px 0;color:#cbd5e1}
</style>
</head>
<body>
<main>
<h1>PartsPortal</h1>
<div class="version">Current release: $(Html $Version)</div>
<a href="$(Html $msiName)">Download PartsPortal $(Html $Version)</a>
<h2>What's new</h2>
<ul>
$bullets
</ul>
</main>
</body>
</html>
"@

    Set-Content $InstallPage $page -Encoding UTF8

    Step "Building PartsPortal"

    Run "dotnet" @(
        "build",
        "src\PartsPortal.Contracts\PartsPortal.Contracts.csproj",
        "-c", "Release"
    )

    Run "dotnet" @(
        "build",
        "src\PartsPortal.Api\PartsPortal.Api.csproj",
        "-c", "Release"
    )

    Run "dotnet" @(
        "build",
        "src\PartsPortal.Client\PartsPortal.Client.csproj",
        "-c", "Release"
    )

    Step "Publishing Windows client"

    Run "dotnet" @(
        "publish",
        "src\PartsPortal.Client\PartsPortal.Client.csproj",
        "-c", "Release",
        "-r", "win-x64",
        "--self-contained", "true",
        "-p:PublishProfile=FolderProfile"
    )

    Step "Building MSI installer"

    $devenv = Find-Devenv
    $setupLog = Join-Path $Repo "artifacts\setup-build.log"
    New-Item -ItemType Directory -Path (Split-Path $setupLog -Parent) -Force | Out-Null
    Remove-Item $setupLog -Force -ErrorAction SilentlyContinue

    & $devenv $Solution "/Build" "Release|Any CPU" "/Project" "PartsPortal.Setup" "/ProjectConfig" "Release" "/Out" $setupLog

    if ($LASTEXITCODE -ne 0) {
        Write-Host ""
        Write-Host "Visual Studio setup build failed. Last build-log lines:" -ForegroundColor Red

        if (Test-Path $setupLog) {
            Get-Content $setupLog -Tail 120 | ForEach-Object { Write-Host $_ }
        }
        else {
            Write-Host "Visual Studio did not create $setupLog"
        }

        throw "PartsPortal.Setup failed with exit code $LASTEXITCODE."
    }

    $msiPath = Join-Path $Repo "PartsPortal.Setup\Release\$msiName"

    if (-not (Test-Path $msiPath)) {
        throw "Installer was not found at $msiPath"
    }

    if (-not $SkipDeploy) {
        Step "Creating release package"

        $artifactDir = Join-Path $Repo "artifacts"
        $packageDir = Join-Path $artifactDir "PartsPortal-$Version"
        $zipPath = Join-Path $artifactDir "PartsPortal-Release-$Version.zip"

        if (Test-Path $packageDir) {
            Remove-Item $packageDir -Recurse -Force
        }

        New-Item -ItemType Directory -Path $packageDir -Force | Out-Null
        New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

        Copy-Item $msiPath (Join-Path $packageDir $msiName)
        Copy-Item $InstallPage (Join-Path $packageDir "index.html")

        $policy = [System.Collections.Generic.List[string]]::new()
        $policy.Add("export ClientVersion__LatestVersion=$(ShellValue $Version)")
        $policy.Add("export ClientVersion__MinimumSupportedVersion=$(ShellValue $MinimumSupportedVersion)")
        $policy.Add("export ClientVersion__DownloadUrl=$(ShellValue $downloadUrl)")

        for ($i = 0; $i -lt $ReleaseNotes.Count; $i++) {
            $policy.Add("export ClientVersion__ReleaseNotes__$i=$(ShellValue $ReleaseNotes[$i])")
        }

        Write-Utf8NoBom `
            (Join-Path $packageDir "policy.env") `
            ($policy -join "`n")

        # Generated dynamically so there is only one source file to maintain.
        $remoteHelper = @'
#!/bin/bash
set -euo pipefail

VERSION="$1"

if [ -z "$VERSION" ]; then
    echo "Usage: install-release.sh <version>"
    exit 1
fi

BASE_DIR="$(cd "$(dirname "$0")" && pwd)"
MSI_NAME="PartsPortal-Setup-$VERSION.msi"
INSTALL_DIR="/var/www/partsportal/install"
ENV_FILE="/etc/partsportal/partsportal-api.env"
STAMP="$(date +%Y%m%d-%H%M%S)"

ENV_BACKUP="$ENV_FILE.bak-$STAMP"
INDEX_BACKUP="$INSTALL_DIR/index.html.bak-$STAMP"
MSI_BACKUP="$INSTALL_DIR/$MSI_NAME.bak-$STAMP"

test -f "$BASE_DIR/$MSI_NAME"
test -f "$BASE_DIR/index.html"
test -f "$BASE_DIR/policy.env"

mkdir -p "$INSTALL_DIR"

cp "$ENV_FILE" "$ENV_BACKUP"

if [ -f "$INSTALL_DIR/index.html" ]; then
    cp "$INSTALL_DIR/index.html" "$INDEX_BACKUP"
fi

if [ -f "$INSTALL_DIR/$MSI_NAME" ]; then
    cp "$INSTALL_DIR/$MSI_NAME" "$MSI_BACKUP"
fi

rollback() {
    trap - ERR
    echo "Deployment failed. Restoring the previous PartsPortal server policy."

    cp "$ENV_BACKUP" "$ENV_FILE"
    chown root:partsportal "$ENV_FILE"
    chmod 0640 "$ENV_FILE"

    if [ -f "$INDEX_BACKUP" ]; then
        cp "$INDEX_BACKUP" "$INSTALL_DIR/index.html"
    fi

    if [ -f "$MSI_BACKUP" ]; then
        cp "$MSI_BACKUP" "$INSTALL_DIR/$MSI_NAME"
    else
        rm -f "$INSTALL_DIR/$MSI_NAME"
    fi

    service partsportal-api restart || true
}

trap rollback ERR

install -m 0644 "$BASE_DIR/$MSI_NAME" "$INSTALL_DIR/$MSI_NAME"
install -m 0644 "$BASE_DIR/index.html" "$INSTALL_DIR/index.html"

grep -v '^export ClientVersion__' "$ENV_FILE" > "$ENV_FILE.new"
cat "$BASE_DIR/policy.env" >> "$ENV_FILE.new"

chown root:partsportal "$ENV_FILE.new"
chmod 0640 "$ENV_FILE.new"
mv "$ENV_FILE.new" "$ENV_FILE"

service partsportal-api restart
sleep 3

curl -fsS http://127.0.0.1:5085/api/system/client-version |
    grep -Fq "\"latestVersion\":\"$VERSION\""

curl -fsSI "http://127.0.0.1/partsportal/install/$MSI_NAME" >/dev/null

trap - ERR

echo
echo "PartsPortal $VERSION deployed successfully."
'@

        Write-Utf8NoBom `
            (Join-Path $packageDir "install-release.sh") `
            $remoteHelper

        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }

        Compress-Archive `
            -Path (Join-Path $packageDir "*") `
            -DestinationPath $zipPath `
            -Force

        Step "Uploading release package"

        $remoteZip = "{0}@{1}:~/PartsPortal-Release-{2}.zip" -f $VmUser, $Vm, $Version
        Run "scp" @($zipPath, $remoteZip)

        Step "Deploying release on VM"

        $remoteCommand =
            "rm -rf ~/partsportal-release-$Version && " +
            "mkdir -p ~/partsportal-release-$Version && " +
            "unzip -q -o ~/PartsPortal-Release-$Version.zip -d ~/partsportal-release-$Version && " +
            "sudo bash ~/partsportal-release-$Version/install-release.sh $Version && " +
            "rm -rf ~/partsportal-release-$Version ~/PartsPortal-Release-$Version.zip"

        Run "ssh" @("-t", "$VmUser@$Vm", $remoteCommand)

        Step "Verifying deployment from workstation"

        $live = Invoke-RestMethod $VersionApi -TimeoutSec 10

        if ([string]$live.latestVersion -ne $Version) {
            throw "Server reports latestVersion '$($live.latestVersion)' instead of '$Version'."
        }

        $head = Invoke-WebRequest $downloadUrl -Method Head -TimeoutSec 20

        if ($head.StatusCode -ne 200) {
            throw "Installer URL returned HTTP $($head.StatusCode)."
        }

        $keepLocalReleaseChanges = $true
    }
    else {
        $keepLocalReleaseChanges = $true
        Write-Host ""
        Write-Host "Build complete. Server deployment skipped." -ForegroundColor Yellow
    }

    if (-not $SkipGit) {
        Step "Committing and pushing Git"

        Run "git" @("add", "-A")

        & git diff --cached --quiet

        if ($LASTEXITCODE -ne 0) {
            Run "git" @("commit", "-m", "Release PartsPortal $Version")
        }

        Run "git" @("push")
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " PartsPortal $Version release complete" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green

    if (-not $SkipDeploy) {
        Write-Host "Download: $downloadUrl"
    }
}
catch {
    if (-not $keepLocalReleaseChanges) {
        Write-Host ""
        Write-Host "Release failed before completion. Restoring local version files..." -ForegroundColor Yellow

        Set-Content $ClientProject $originalClient -Encoding UTF8
        Set-Content $ApiSettings $originalApiSettings -Encoding UTF8
        Set-Content $SetupProject $originalSetup -Encoding UTF8
        Set-Content $InstallPage $originalIndex -Encoding UTF8
    }

    Write-Host ""
    Write-Host "PUSH UPDATE FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    throw
}
