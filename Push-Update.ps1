param(
    [string]$Version,
    [string[]]$ReleaseNotes,
    [switch]$SkipGit,
    [switch]$SkipDeploy
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$Repo = $PSScriptRoot
$Vm = "10.130.206.135"
$VmUser = "admin"
$InstallBase = "http://$Vm/partsportal/install"
$VersionApi = "http://$Vm/partsportal/api/system/client-version"

$ClientProject = Join-Path $Repo "src\PartsPortal.Client\PartsPortal.Client.csproj"
$ApiSettings = Join-Path $Repo "src\PartsPortal.Api\appsettings.json"
$ClickOnceProfile = Join-Path $Repo "src\PartsPortal.Client\Properties\PublishProfiles\ClickOnceProfile.pubxml"
$InstallPage = Join-Path $Repo "deploy\install\index.html"
$ClickOnceOutput = Join-Path $Repo "src\PartsPortal.Client\bin\Release\net8.0-windows\win-x64\clickonce"

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
    [xml]$xml = Get-Content $ClientProject
    return ([string]$xml.Project.PropertyGroup.Version).Trim()
}

function Next-Patch([string]$Value) {
    $v = [Version]::Parse($Value)
    return "$($v.Major).$($v.Minor).$($v.Build + 1)"
}

function Validate-Version([string]$Value) {
    $parsed = $null
    if (-not [Version]::TryParse($Value, [ref]$parsed) -or $Value.Split('.').Count -ne 3) {
        throw "Version must use three parts, for example 3.2.10."
    }
}

function Html([string]$Value) {
    return [System.Net.WebUtility]::HtmlEncode($Value)
}

function ShellValue([string]$Value) {
    $Value = $Value.Replace("\", "\\").Replace('"', '\"').Replace('$', '\$')
    return '"' + $Value + '"'
}

function Write-Utf8NoBom([string]$Path, [string]$Content) {
    $Content = $Content.Replace("`r`n", "`n").Replace("`r", "`n")
    $encoding = New-Object System.Text.UTF8Encoding($false)
    [System.IO.File]::WriteAllText($Path, $Content, $encoding)
}

function Binary-ContainsText([string]$Path, [string]$Text) {
    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $ascii = [System.Text.Encoding]::ASCII.GetString($bytes)
    if ($ascii.IndexOf($Text, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) { return $true }
    $unicode = [System.Text.Encoding]::Unicode.GetString($bytes)
    return $unicode.IndexOf($Text, [System.StringComparison]::OrdinalIgnoreCase) -ge 0
}


function Write-TextWithRetry(
    [string]$Path,
    [string]$Content,
    [int]$Attempts = 12,
    [int]$DelayMilliseconds = 500
) {
    $encoding = New-Object System.Text.UTF8Encoding($true)

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            [System.IO.File]::WriteAllText($Path, $Content, $encoding)
            return
        }
        catch [System.IO.IOException] {
            if ($attempt -eq $Attempts) {
                throw "Could not update '$Path' after $Attempts attempts because another process still has it locked."
            }

            Write-Host "File is temporarily locked; retrying ($attempt/$Attempts): $Path" -ForegroundColor Yellow
            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}
function Remove-PathWithRetry(
    [string]$Path,
    [int]$Attempts = 12,
    [int]$DelayMilliseconds = 500
) {
    if (-not (Test-Path $Path)) {
        return
    }

    for ($attempt = 1; $attempt -le $Attempts; $attempt++) {
        try {
            Remove-Item $Path -Recurse -Force -ErrorAction Stop
            return
        }
        catch {
            if ($attempt -eq $Attempts) {
                throw "Could not clean '$Path' after $Attempts attempts. Close Visual Studio and retry."
            }

            Write-Host "Build output is temporarily locked; retrying ($attempt/$Attempts): $Path" -ForegroundColor Yellow
            Start-Sleep -Milliseconds $DelayMilliseconds
        }
    }
}

function Find-MSBuild {
    # ClickOnce publishing requires Visual Studio MSBuild.
    # Ignore SQL Server Management Studio MSBuild because it cannot resolve Microsoft.NET.Sdk.

    $vswhereCandidates = @(
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Microsoft Visual Studio\Installer\vswhere.exe"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Microsoft Visual Studio\Installer\vswhere.exe")
    ) | Select-Object -Unique

    foreach ($vswhere in $vswhereCandidates) {
        if (-not (Test-Path $vswhere)) { continue }

        $found = & $vswhere -latest -prerelease -products * -requires Microsoft.Component.MSBuild -find "MSBuild\Current\Bin\MSBuild.exe" 2>$null | Select-Object -First 1

        if ($found -and (Test-Path $found)) {
            Write-Host "Using Visual Studio MSBuild: $found" -ForegroundColor DarkGray
            return $found
        }
    }

    $roots = @(
        (Join-Path ([Environment]::GetFolderPath("ProgramFiles")) "Microsoft Visual Studio"),
        (Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Microsoft Visual Studio")
    ) | Select-Object -Unique

    foreach ($root in $roots) {
        if (-not (Test-Path $root)) { continue }

        $found = Get-ChildItem $root -Filter "MSBuild.exe" -File -Recurse -ErrorAction SilentlyContinue |
            Where-Object {
                $_.FullName -match "Microsoft Visual Studio" -and
                $_.FullName -match "MSBuild\\Current\\Bin\\MSBuild\.exe$"
            } |
            Select-Object -First 1

        if ($found) {
            Write-Host "Using Visual Studio MSBuild: $($found.FullName)" -ForegroundColor DarkGray
            return $found.FullName
        }
    }

    $cmd = Get-Command "msbuild.exe" -ErrorAction SilentlyContinue
    if ($cmd -and (Test-Path $cmd.Source) -and $cmd.Source -match "Microsoft Visual Studio") {
        Write-Host "Using Visual Studio MSBuild: $($cmd.Source)" -ForegroundColor DarkGray
        return $cmd.Source
    }

    throw "Visual Studio MSBuild.exe was not found. SQL Server Management Studio MSBuild is intentionally ignored."
}

Set-Location $Repo

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
Validate-Version $Version

if (-not $ReleaseNotes -or $ReleaseNotes.Count -eq 0) {
    $notes = [System.Collections.Generic.List[string]]::new()

    Write-Host ""
    Write-Host "Enter release notes one at a time. Press Enter on a blank line when finished." -ForegroundColor Yellow

    while ($true) {
        $note = Read-Host "Release note"
        if ([string]::IsNullOrWhiteSpace($note)) { break }
        $notes.Add($note.Trim())
    }

    if ($notes.Count -eq 0) {
        $notes.Add("PartsPortal $Version release.")
    }

    $ReleaseNotes = $notes.ToArray()
}

$applicationVersion = "$Version.0"
$deploymentUrl = "$InstallBase/PartsPortal.application"

Write-Host ""
Write-Host "PartsPortal ClickOnce release" -ForegroundColor Green
Write-Host "  Current version : $current"
Write-Host "  New version     : $Version"
Write-Host "  ClickOnce       : $applicationVersion"
Write-Host "  VM              : $Vm"
Write-Host ""

$confirm = Read-Host "Build and push this ClickOnce release? [Y/n]"
if ($confirm -match "^(n|no)$") {
    Write-Host "Cancelled."
    exit 0
}

$originalClient = Get-Content $ClientProject -Raw
$originalApiSettings = Get-Content $ApiSettings -Raw
$originalProfile = Get-Content $ClickOnceProfile -Raw
$originalIndex = Get-Content $InstallPage -Raw
$keepChanges = $false

try {
    Step "Updating client and ClickOnce versions"

    $clientText = $originalClient
    $clientText = [regex]::Replace($clientText, "<Version>.*?</Version>", "<Version>$Version</Version>", 1)
    $clientText = [regex]::Replace($clientText, "<AssemblyVersion>.*?</AssemblyVersion>", "<AssemblyVersion>$Version</AssemblyVersion>", 1)
    $clientText = [regex]::Replace($clientText, "<FileVersion>.*?</FileVersion>", "<FileVersion>$Version</FileVersion>", 1)
    $clientText = [regex]::Replace($clientText, "<InformationalVersion>.*?</InformationalVersion>", "<InformationalVersion>$Version (Released)</InformationalVersion>", 1)
    Write-TextWithRetry $ClientProject $clientText

    $profileText = $originalProfile
    $profileText = [regex]::Replace(
        $profileText,
        "<ApplicationVersion>.*?</ApplicationVersion>",
        "<ApplicationVersion>$applicationVersion</ApplicationVersion>",
        1)
    Write-TextWithRetry $ClickOnceProfile $profileText

    Step "Updating release metadata"

    $settings = Get-Content $ApiSettings -Raw | ConvertFrom-Json
    $settings.ClientVersion.LatestVersion = $Version
    $settings.ClientVersion.MinimumSupportedVersion = $Version
    $settings.ClientVersion.DownloadUrl = $deploymentUrl
    $settings.ClientVersion.ReleaseNotes = @($ReleaseNotes)
    $settingsJson = $settings | ConvertTo-Json -Depth 10
    Write-TextWithRetry $ApiSettings $settingsJson

    $bullets = ($ReleaseNotes | ForEach-Object {
        "  <li>$(Html $_)</li>"
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
.note{margin-top:24px;color:#94a3b8;font-size:13px;line-height:1.5}
</style>
</head>
<body>
<main>
<h1>PartsPortal</h1>
<div class="version">Current release: $(Html $Version)</div>
<a href="PartsPortal.application">Install / Update PartsPortal</a>
<h2>What's new</h2>
<ul>
$bullets
</ul>
<div class="note">Install PartsPortal once with ClickOnce. Future releases are checked automatically when PartsPortal starts.</div>
</main>
</body>
</html>
"@
    Write-TextWithRetry $InstallPage $page

    Step "Building Contracts and API"
    Run "dotnet" @("build", "src\PartsPortal.Contracts\PartsPortal.Contracts.csproj", "-c", "Release")
    Run "dotnet" @("build", "src\PartsPortal.Api\PartsPortal.Api.csproj", "-c", "Release")

    Step "Cleaning client build output"

    # Removed manifests can survive in stale obj/bin output, so ClickOnce must start clean.
    & dotnet build-server shutdown | Out-Null

    $clientBin = Join-Path $Repo "src\PartsPortal.Client\bin"
    $clientObj = Join-Path $Repo "src\PartsPortal.Client\obj"

    Remove-PathWithRetry $clientBin
    Remove-PathWithRetry $clientObj

    Step "Publishing ClickOnce client"

    $msbuild = Find-MSBuild
    Write-Host "Using MSBuild: $msbuild" -ForegroundColor DarkGray

    Run $msbuild @(
        $ClientProject,
        "/t:Publish",
        "/p:Configuration=Release",
        "/p:PublishProfile=ClickOnceProfile",
        "/p:ApplicationVersion=$applicationVersion",
        "/p:RuntimeIdentifier=win-x64",
        "/p:SelfContained=true",
        "/p:LauncherBasedDeployment=true",
        "/restore"
    )

    $manifest = Join-Path $ClickOnceOutput "PartsPortal.application"
    if (-not (Test-Path $manifest)) {
        throw "ClickOnce publish did not create PartsPortal.application in $ClickOnceOutput"
    }

    $appFiles = Join-Path $ClickOnceOutput "Application Files"
    if (-not (Test-Path $appFiles)) {
        throw "ClickOnce publish did not create the Application Files directory."
    }

    Step "Validating generated ClickOnce manifest"

    [xml]$deploymentXml = Get-Content $manifest -Raw
    $appDependency = $deploymentXml.SelectSingleNode("//*[local-name()='dependentAssembly' and @dependencyType='install']")

    if (-not $appDependency) {
        throw "ClickOnce deployment manifest does not reference an install application manifest."
    }

    $applicationManifestCodebase = [string]$appDependency.codebase
    if ([string]::IsNullOrWhiteSpace($applicationManifestCodebase)) {
        throw "ClickOnce deployment manifest is missing the application manifest codebase."
    }

    $applicationManifestRelativePath = $applicationManifestCodebase.Replace("/", [System.IO.Path]::DirectorySeparatorChar).Replace("\\", [System.IO.Path]::DirectorySeparatorChar)
    $generatedAppManifestPath = Join-Path $ClickOnceOutput $applicationManifestRelativePath

    if (-not (Test-Path $generatedAppManifestPath)) {
        # Some ClickOnce publish modes map payload files with a .deploy suffix.
        $mappedPath = "$generatedAppManifestPath.deploy"
        if (Test-Path $mappedPath) {
            $generatedAppManifestPath = $mappedPath
        }
        else {
            throw "Referenced ClickOnce application manifest was not found: $applicationManifestRelativePath"
        }
    }

    $generatedManifestText = Get-Content $generatedAppManifestPath -Raw
    $requestedPrivilegesCount = [regex]::Matches($generatedManifestText, "<requestedPrivileges\b", [System.Text.RegularExpressions.RegexOptions]::IgnoreCase).Count

    if ($requestedPrivilegesCount -gt 1) {
        throw "Generated ClickOnce manifest is invalid: found $requestedPrivilegesCount requestedPrivileges elements. Deployment was stopped before upload."
    }

    Write-Host "Manifest validated: $requestedPrivilegesCount requestedPrivileges element(s)." -ForegroundColor Green
    Write-Host "Application manifest: $generatedAppManifestPath" -ForegroundColor DarkGray

    [xml]$applicationManifestXml = Get-Content $generatedAppManifestPath -Raw
    $commandLineNode = $applicationManifestXml.SelectSingleNode("//*[local-name()='entryPoint']/*[local-name()='commandLine']")

    if (-not $commandLineNode) {
        throw "Generated ClickOnce application manifest has no commandLine entry point. Deployment was stopped before upload."
    }

    $entryPointFile = [string]$commandLineNode.file
    if ([string]::IsNullOrWhiteSpace($entryPointFile)) {
        throw "Generated ClickOnce application manifest has an empty commandLine file. Deployment was stopped before upload."
    }

    if (-not $entryPointFile.Equals("Launcher.exe", [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Generated ClickOnce entry point is '$entryPointFile' instead of expected launcher 'Launcher.exe'. Deployment was stopped before upload."
    }

    $applicationManifestDirectory = Split-Path $generatedAppManifestPath -Parent
    $entryPointPath = Join-Path $applicationManifestDirectory $entryPointFile

    if (-not (Test-Path $entryPointPath)) {
        $mappedEntryPointPath = "$entryPointPath.deploy"
        if (Test-Path $mappedEntryPointPath) {
            $entryPointPath = $mappedEntryPointPath
        }
        else {
            throw "Generated ClickOnce entry point '$entryPointFile' does not exist beside the application manifest. Deployment was stopped before upload."
        }
    }

    if (-not (Binary-ContainsText $entryPointPath "PartsPortal.exe")) {
        if (Binary-ContainsText $entryPointPath "apphost.exe") {
            throw "Launcher.exe is wired to apphost.exe instead of PartsPortal.exe. Deployment was stopped before upload."
        }
        throw "Could not verify Launcher.exe handoff target PartsPortal.exe. Deployment was stopped before upload."
    }

    Write-Host "ClickOnce entry point: $entryPointFile" -ForegroundColor Green
    Write-Host "Launcher handoff target: PartsPortal.exe" -ForegroundColor Green
    Write-Host "Launcher.exe and MSIL manifest identity are expected for .NET launcher-based ClickOnce." -ForegroundColor DarkGray




    if ($SkipDeploy) {
        $keepChanges = $true
        Write-Host ""
        Write-Host "ClickOnce publish completed. Deployment skipped." -ForegroundColor Yellow
    }
    else {
        Step "Preparing ClickOnce deployment"

        Copy-Item $InstallPage (Join-Path $ClickOnceOutput "index.html") -Force

        $policy = [System.Collections.Generic.List[string]]::new()
        $policy.Add("export ClientVersion__LatestVersion=$(ShellValue $Version)")
        $policy.Add("export ClientVersion__MinimumSupportedVersion=$(ShellValue $Version)")
        $policy.Add("export ClientVersion__DownloadUrl=$(ShellValue $deploymentUrl)")

        for ($i = 0; $i -lt $ReleaseNotes.Count; $i++) {
            $policy.Add("export ClientVersion__ReleaseNotes__$i=$(ShellValue $ReleaseNotes[$i])")
        }

        Write-Utf8NoBom (Join-Path $ClickOnceOutput "policy.env") ($policy -join "`n")

        $remoteStage = "/home/$VmUser/partsportal-clickonce-$Version"
        $remoteScript = "/home/$VmUser/partsportal-clickonce-deploy-$PID.sh"
        $localScript = Join-Path $env:TEMP "partsportal-clickonce-deploy-$PID.sh"

        $deployScript = @'
#!/bin/bash
set -euo pipefail

VERSION="$1"
STAGE_DIR="$2"
INSTALL_DIR="/var/www/partsportal/install"
ENV_FILE="/etc/partsportal/partsportal-api.env"
STAMP="$(date +%Y%m%d-%H%M%S)"
ENV_BACKUP="$ENV_FILE.bak-clickonce-$STAMP"
BACKUP_DIR="/var/www/partsportal/install-backup"

test -f "$STAGE_DIR/PartsPortal.application"
test -d "$STAGE_DIR/Application Files"
test -f "$STAGE_DIR/index.html"
test -f "$STAGE_DIR/policy.env"

cp "$ENV_FILE" "$ENV_BACKUP"
rm -rf "$BACKUP_DIR"
mkdir -p "$BACKUP_DIR"
if [ -d "$INSTALL_DIR" ]; then
    cp -a "$INSTALL_DIR/." "$BACKUP_DIR/"
fi

rollback() {
    trap - ERR
    echo "ClickOnce deployment failed. Restoring previous server files."
    cp "$ENV_BACKUP" "$ENV_FILE"
    rm -rf "$INSTALL_DIR"
    mkdir -p "$INSTALL_DIR"
    if [ -d "$BACKUP_DIR" ]; then
        cp -a "$BACKUP_DIR/." "$INSTALL_DIR/"
    fi
    chown root:partsportal "$ENV_FILE"
    chmod 0640 "$ENV_FILE"
    service partsportal-api restart || true
}

trap rollback ERR

mkdir -p "$INSTALL_DIR"
rm -rf "$INSTALL_DIR/Application Files"
cp -a "$STAGE_DIR/." "$INSTALL_DIR/"

grep -v '^export ClientVersion__' "$ENV_FILE" > "$ENV_FILE.new"
cat "$INSTALL_DIR/policy.env" >> "$ENV_FILE.new"
chown root:partsportal "$ENV_FILE.new"
chmod 0640 "$ENV_FILE.new"
mv "$ENV_FILE.new" "$ENV_FILE"
rm -f "$INSTALL_DIR/policy.env"

chown -R root:root "$INSTALL_DIR"
chmod -R a+rX "$INSTALL_DIR"

service partsportal-api restart
sleep 3

curl -fsS http://127.0.0.1:5085/api/system/client-version |
    grep -Fq "\"latestVersion\":\"$VERSION\""
curl -fsSI http://127.0.0.1/partsportal/install/PartsPortal.application >/dev/null

trap - ERR
echo
echo "PartsPortal $VERSION ClickOnce deployment completed successfully."
'@

        Write-Utf8NoBom $localScript $deployScript

        try {
            Step "Preparing VM staging folder"
            Run "ssh" @("$VmUser@$Vm", "rm -rf $remoteStage && mkdir -p $remoteStage")

            Step "Uploading ClickOnce release"
            foreach ($item in Get-ChildItem $ClickOnceOutput) {
                $target = "{0}@{1}:{2}/" -f $VmUser, $Vm, $remoteStage

                if ($item.PSIsContainer) {
                    Run "scp" @("-r", $item.FullName, $target)
                }
                else {
                    Run "scp" @($item.FullName, $target)
                }
            }

            Run "scp" @($localScript, ("{0}@{1}:{2}" -f $VmUser, $Vm, $remoteScript))

            Step "Deploying ClickOnce release on VM"
            $deployCommand = "sudo bash $remoteScript '$Version' '$remoteStage' && rm -rf $remoteStage $remoteScript"
            Run "ssh" @("-t", "$VmUser@$Vm", $deployCommand)
        }
        finally {
            Remove-Item $localScript -Force -ErrorAction SilentlyContinue
        }

        Step "Verifying ClickOnce deployment"
        Start-Sleep -Seconds 3

        $head = Invoke-WebRequest $deploymentUrl -Method Head -TimeoutSec 20
        if ($head.StatusCode -ne 200) {
            throw "PartsPortal.application returned HTTP $($head.StatusCode)."
        }

        $live = Invoke-RestMethod $VersionApi -TimeoutSec 10
        if ([string]$live.latestVersion -ne $Version) {
            throw "Version API reports '$($live.latestVersion)' instead of '$Version'."
        }

        $keepChanges = $true
    }

    if (-not $SkipGit) {
        Step "Committing and pushing Git"
        Run "git" @("add", "-A")

        & git diff --cached --quiet
        if ($LASTEXITCODE -ne 0) {
            Run "git" @("commit", "-m", "Release PartsPortal $Version via ClickOnce")
        }

        Run "git" @("push")
    }

    Write-Host ""
    Write-Host "========================================" -ForegroundColor Green
    Write-Host " PartsPortal $Version ClickOnce release complete" -ForegroundColor Green
    Write-Host "========================================" -ForegroundColor Green
    Write-Host "Install: $deploymentUrl"
}
catch {
    if (-not $keepChanges) {
        Write-Host ""
        Write-Host "Release failed. Restoring local release files..." -ForegroundColor Yellow

        Write-TextWithRetry $ClientProject $originalClient
        Write-TextWithRetry $ApiSettings $originalApiSettings
        Write-TextWithRetry $ClickOnceProfile $originalProfile
        Write-TextWithRetry $InstallPage $originalIndex
    }

    Write-Host ""
    Write-Host "CLICKONCE RELEASE FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    throw
}
