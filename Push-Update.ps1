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

function Find-MSBuild {
    $cmd = Get-Command "msbuild.exe" -ErrorAction SilentlyContinue
    if ($cmd -and (Test-Path $cmd.Source)) {
        return $cmd.Source
    }

    $vswhere = Join-Path ([Environment]::GetFolderPath("ProgramFilesX86")) "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswhere) {
        $found = & $vswhere -latest -prerelease -products * -find "MSBuild\Current\Bin\MSBuild.exe" 2>$null |
            Select-Object -First 1
        if ($found -and (Test-Path $found)) {
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
            Where-Object { $_.FullName -match "MSBuild\\Current\\Bin\\MSBuild\.exe$" } |
            Select-Object -First 1

        if ($found) {
            return $found.FullName
        }
    }

    throw "MSBuild.exe was not found."
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
    Set-Content $ClientProject $clientText -Encoding UTF8

    $profileText = $originalProfile
    $profileText = [regex]::Replace(
        $profileText,
        "<ApplicationVersion>.*?</ApplicationVersion>",
        "<ApplicationVersion>$applicationVersion</ApplicationVersion>",
        1)
    Set-Content $ClickOnceProfile $profileText -Encoding UTF8

    Step "Updating release metadata"

    $settings = Get-Content $ApiSettings -Raw | ConvertFrom-Json
    $settings.ClientVersion.LatestVersion = $Version
    $settings.ClientVersion.MinimumSupportedVersion = $Version
    $settings.ClientVersion.DownloadUrl = $deploymentUrl
    $settings.ClientVersion.ReleaseNotes = @($ReleaseNotes)
    $settings | ConvertTo-Json -Depth 10 | Set-Content $ApiSettings -Encoding UTF8

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
    Set-Content $InstallPage $page -Encoding UTF8

    Step "Building Contracts and API"
    Run "dotnet" @("build", "src\PartsPortal.Contracts\PartsPortal.Contracts.csproj", "-c", "Release")
    Run "dotnet" @("build", "src\PartsPortal.Api\PartsPortal.Api.csproj", "-c", "Release")

    Step "Publishing ClickOnce client"
    if (Test-Path $ClickOnceOutput) {
        Remove-Item $ClickOnceOutput -Recurse -Force
    }

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

    if ($SkipDeploy) {
        $keepChanges = $true
        Write-Host ""
        Write-Host "ClickOnce publish completed. Deployment skipped." -ForegroundColor Yellow
    }
    else {
        Step "Creating ClickOnce release ZIP"

        $artifactDir = Join-Path $Repo "artifacts"
        $zipPath = Join-Path $artifactDir "PartsPortal-ClickOnce-$Version.zip"
        New-Item -ItemType Directory -Path $artifactDir -Force | Out-Null

        Copy-Item $InstallPage (Join-Path $ClickOnceOutput "index.html") -Force

        $policy = [System.Collections.Generic.List[string]]::new()
        $policy.Add("export ClientVersion__LatestVersion=$(ShellValue $Version)")
        $policy.Add("export ClientVersion__MinimumSupportedVersion=$(ShellValue $Version)")
        $policy.Add("export ClientVersion__DownloadUrl=$(ShellValue $deploymentUrl)")

        for ($i = 0; $i -lt $ReleaseNotes.Count; $i++) {
            $policy.Add("export ClientVersion__ReleaseNotes__$i=$(ShellValue $ReleaseNotes[$i])")
        }

        Write-Utf8NoBom (Join-Path $ClickOnceOutput "policy.env") ($policy -join "`n")

        if (Test-Path $zipPath) {
            Remove-Item $zipPath -Force
        }

        Compress-Archive -Path (Join-Path $ClickOnceOutput "*") -DestinationPath $zipPath -Force

        Step "Uploading ClickOnce release"
        $remoteZip = "{0}@{1}:~/PartsPortal-ClickOnce-{2}.zip" -f $VmUser, $Vm, $Version
        Run "scp" @($zipPath, $remoteZip)

        Step "Deploying ClickOnce release on VM"

        $remote = @"
set -e
rm -rf ~/partsportal-clickonce-$Version
mkdir -p ~/partsportal-clickonce-$Version
unzip -q -o ~/PartsPortal-ClickOnce-$Version.zip -d ~/partsportal-clickonce-$Version

sudo mkdir -p /var/www/partsportal/install
sudo cp /etc/partsportal/partsportal-api.env /etc/partsportal/partsportal-api.env.bak-clickonce
sudo rm -rf /var/www/partsportal/install/Application\ Files
sudo cp -R ~/partsportal-clickonce-$Version/. /var/www/partsportal/install/

sudo sh -c "grep -v '^export ClientVersion__' /etc/partsportal/partsportal-api.env > /etc/partsportal/partsportal-api.env.new"
sudo sh -c "cat /var/www/partsportal/install/policy.env >> /etc/partsportal/partsportal-api.env.new"
sudo chown root:partsportal /etc/partsportal/partsportal-api.env.new
sudo chmod 640 /etc/partsportal/partsportal-api.env.new
sudo mv /etc/partsportal/partsportal-api.env.new /etc/partsportal/partsportal-api.env
sudo rm -f /var/www/partsportal/install/policy.env
sudo chown -R root:root /var/www/partsportal/install
sudo find /var/www/partsportal/install -type d -exec chmod 755 {} \;
sudo find /var/www/partsportal/install -type f -exec chmod 644 {} \;

sudo service partsportal-api restart

rm -rf ~/partsportal-clickonce-$Version ~/PartsPortal-ClickOnce-$Version.zip
"@

        Run "ssh" @("-t", "$VmUser@$Vm", $remote)

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

        Set-Content $ClientProject $originalClient -Encoding UTF8
        Set-Content $ApiSettings $originalApiSettings -Encoding UTF8
        Set-Content $ClickOnceProfile $originalProfile -Encoding UTF8
        Set-Content $InstallPage $originalIndex -Encoding UTF8
    }

    Write-Host ""
    Write-Host "CLICKONCE RELEASE FAILED" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    throw
}
