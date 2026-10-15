param(
    [string]$Vm = "10.130.206.135",
    [string]$VmUser = "admin"
)

$ErrorActionPreference = "Stop"

$Repo = Split-Path -Parent $MyInvocation.MyCommand.Path
$ApiProject = Join-Path $Repo "src\PartsPortal.Api\PartsPortal.Api.csproj"
$PublishDir = Join-Path $Repo "artifacts\api-linux-x64"

$bundleRoot = Join-Path $env:TEMP "partsportal-api-bundle-$PID"
$bundleApi = Join-Path $bundleRoot "api"
$deployScript = Join-Path $bundleRoot "deploy.sh"
$bundlePath = Join-Path $env:TEMP "partsportal-api-$PID.tar.gz"
$remoteBundle = "/home/$VmUser/partsportal-api-$PID.tar.gz"
$remoteStage = "/home/$VmUser/partsportal-api-stage-$PID"

function Step([string]$message) {
    Write-Host ""
    Write-Host "==> $message" -ForegroundColor Cyan
}

function Run([string]$exe, [string[]]$arguments) {
    & $exe @arguments

    if ($LASTEXITCODE -ne 0) {
        throw "$exe failed with exit code $LASTEXITCODE."
    }
}

function Write-Utf8NoBom([string]$path, [string]$content) {
    $encoding = New-Object System.Text.UTF8Encoding($false)

    # This file is executed by bash on Linux. Force Unix LF line endings.
    $normalized = $content.Replace("`r`n", "`n").Replace("`r", "`n")

    [System.IO.File]::WriteAllText($path, $normalized, $encoding)
}

if (-not (Test-Path $ApiProject)) {
    throw "API project not found: $ApiProject"
}

$remoteScript = @'
#!/bin/bash
set -euo pipefail

STAGE_API="$1"
APP_DIR="/opt/partsportal/api"
BACKUP_DIR="/opt/partsportal/api-backup"

rollback() {
    trap - ERR

    echo
    echo "API deployment failed. Restoring previous API..."

    service partsportal-api stop >/dev/null 2>&1 || true

    if [ -d "$BACKUP_DIR" ]; then
        rm -rf "$APP_DIR"
        cp -a "$BACKUP_DIR" "$APP_DIR"
        chown -R partsportal:partsportal "$APP_DIR"
        chmod +x "$APP_DIR/PartsPortal.Api" || true
        service partsportal-api start || true
    fi
}

trap rollback ERR

test -f "$STAGE_API/PartsPortal.Api"

service partsportal-api stop || true

rm -rf "$BACKUP_DIR"
if [ -d "$APP_DIR" ]; then
    cp -a "$APP_DIR" "$BACKUP_DIR"
fi

rm -rf "$APP_DIR"
mkdir -p "$APP_DIR"
cp -a "$STAGE_API/." "$APP_DIR/"

chown -R partsportal:partsportal "$APP_DIR"
chmod +x "$APP_DIR/PartsPortal.Api"

service partsportal-api start
sleep 3

curl -fsS http://127.0.0.1:5085/api/health

trap - ERR

echo
echo
echo "PartsPortal API deployment completed successfully."
'@

try {
    Step "Publishing PartsPortal API for linux-x64"

    Remove-Item $PublishDir -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -ItemType Directory -Force -Path $PublishDir | Out-Null

    Run "dotnet" @(
        "publish",
        $ApiProject,
        "-c", "Release",
        "-r", "linux-x64",
        "--self-contained", "true",
        "-o", $PublishDir
    )

    Step "Packaging API deployment"

    Remove-Item $bundleRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $bundlePath -Force -ErrorAction SilentlyContinue

    New-Item -ItemType Directory -Force -Path $bundleApi | Out-Null
    Copy-Item (Join-Path $PublishDir "*") $bundleApi -Recurse -Force
    Write-Utf8NoBom $deployScript $remoteScript

    Run "tar" @("-czf", $bundlePath, "-C", $bundleRoot, ".")

    Step "Uploading API bundle"
    Run "scp" @(
        $bundlePath,
        ("{0}@{1}:{2}" -f $VmUser, $Vm, $remoteBundle)
    )

    Step "Deploying API on VM"

    $command =
        "rm -rf '$remoteStage' && " +
        "mkdir -p '$remoteStage' && " +
        "tar -xzf '$remoteBundle' -C '$remoteStage' && " +
        "sed -i 's/\\r$//' '$remoteStage/deploy.sh' && " +
        "sudo bash '$remoteStage/deploy.sh' '$remoteStage/api'; " +
        "rc=`$?; " +
        "rm -rf '$remoteStage' '$remoteBundle'; " +
        "exit `$rc"

    Run "ssh" @("-t", "$VmUser@$Vm", $command)

    Step "Verifying API through nginx"

    $health = Invoke-RestMethod "http://$Vm/partsportal/api/health" -TimeoutSec 15

    Write-Host ""
    Write-Host "Application      : $($health.application)" -ForegroundColor Green
    Write-Host "Status           : $($health.status)" -ForegroundColor Green
    Write-Host "Database status  : $($health.databaseStatus)" -ForegroundColor Green
    Write-Host ""
    Write-Host "PartsPortal API deploy complete." -ForegroundColor Green
}
finally {
    Remove-Item $bundleRoot -Recurse -Force -ErrorAction SilentlyContinue
    Remove-Item $bundlePath -Force -ErrorAction SilentlyContinue
}
