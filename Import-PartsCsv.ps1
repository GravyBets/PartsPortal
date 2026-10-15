param(
    [string]$CsvPath,
    [string]$ApiBaseUrl = "http://10.130.206.135/partsportal/api",
    [switch]$Apply
)

$ErrorActionPreference = "Stop"

function Resolve-PartsCsvPath {
    param([string]$RequestedPath)

    if (-not [string]::IsNullOrWhiteSpace($RequestedPath)) {
        $expanded = [Environment]::ExpandEnvironmentVariables($RequestedPath.Trim())

        if (-not (Test-Path -LiteralPath $expanded -PathType Leaf)) {
            throw "CSV file was not found: $expanded"
        }

        return (Resolve-Path -LiteralPath $expanded).Path
    }

    $settingsDir = Join-Path $env:APPDATA "PartsPortal"

    if (Test-Path $settingsDir) {
        $settingsFiles = Get-ChildItem $settingsDir -Filter "settings_*.json" -File -ErrorAction SilentlyContinue |
            Sort-Object LastWriteTime -Descending

        foreach ($settingsFile in $settingsFiles) {
            try {
                $settings = Get-Content $settingsFile.FullName -Raw | ConvertFrom-Json

                foreach ($propertyName in @("CsvPath", "PartsCsvPath", "LastCsvPath")) {
                    $property = $settings.PSObject.Properties[$propertyName]
                    if (-not $property) {
                        continue
                    }

                    $candidate = [Environment]::ExpandEnvironmentVariables(([string]$property.Value).Trim())

                    if (-not [string]::IsNullOrWhiteSpace($candidate) -and
                        (Test-Path -LiteralPath $candidate -PathType Leaf)) {
                        return (Resolve-Path -LiteralPath $candidate).Path
                    }
                }
            }
            catch {
                continue
            }
        }
    }

    $oneDrive = $env:OneDriveCommercial
    if ([string]::IsNullOrWhiteSpace($oneDrive)) {
        $oneDrive = $env:OneDrive
    }

    if (-not [string]::IsNullOrWhiteSpace($oneDrive) -and (Test-Path $oneDrive)) {
        $candidates = @(
            (Join-Path $oneDrive "Warehouse Parts\PartsList.csv"),
            (Join-Path $oneDrive "Smart Grid Communications\Warehouse Parts\PartsList.csv"),
            (Join-Path $oneDrive "Radio Communications\Smart Grid Communications\Warehouse Parts\PartsList.csv"),
            (Join-Path $oneDrive "Radio Communications\Warehouse Parts\PartsList.csv")
        )

        foreach ($candidate in $candidates) {
            if (Test-Path -LiteralPath $candidate -PathType Leaf) {
                return (Resolve-Path -LiteralPath $candidate).Path
            }
        }
    }

    throw "Could not automatically locate PartsList.csv. Re-run with -CsvPath 'C:\path\to\PartsList.csv'."
}

function Get-Delimiter {
    param([string]$Path)

    foreach ($line in Get-Content -LiteralPath $Path) {
        if ([string]::IsNullOrWhiteSpace($line)) {
            continue
        }

        if ($line.Contains("`t")) {
            return "`t"
        }

        return ","
    }

    return ","
}

function Normalize-WarehouseCode {
    param([string]$Value)

    $name = if ($null -eq $Value) { "" } else { ([string]$Value).Trim() }
    $token = ($name -replace '[^A-Za-z0-9]', '').ToUpperInvariant()

    switch ($token) {
        'WAREHOUSEBUILDINGD' { return "BUILDING_D" }
        'BUILDINGD'          { return "BUILDING_D" }
        'METERLAB'           { return "BUILDING_D" }

        'WAREHOUSEBUILDINGA' { return "BUILDING_A" }
        'BUILDINGA'          { return "BUILDING_A" }
        'CENTRAL'            { return "BUILDING_A" }

        'WAREHOUSEBUILDINGB' { return "BUILDING_B" }
        'BUILDINGB'          { return "BUILDING_B" }
        'SUBSTATION'         { return "BUILDING_B" }

        default {
            return (($name.Trim().ToUpperInvariant()) -replace '[^A-Z0-9]+', '_').Trim('_')
        }
    }
}

function Invoke-Api {
    param(
        [Parameter(Mandatory=$true)][string]$Method,
        [Parameter(Mandatory=$true)][string]$Uri,
        $Body
    )

    $params = @{
        Method = $Method
        Uri = $Uri
        TimeoutSec = 30
    }

    if ($null -ne $Body) {
        $params.ContentType = "application/json"
        $params.Body = ($Body | ConvertTo-Json -Depth 8)
    }

    Invoke-RestMethod @params
}

$resolvedCsv = Resolve-PartsCsvPath $CsvPath
$delimiter = Get-Delimiter $resolvedCsv

Write-Host ""
Write-Host "PartsPortal CSV catalog import" -ForegroundColor Cyan
Write-Host "Importer version: 2026.10.15.3" -ForegroundColor DarkGray
Write-Host "CSV : $resolvedCsv"
Write-Host "API : $ApiBaseUrl"
Write-Host "Mode: $(if ($Apply) { 'APPLY' } else { 'PREVIEW ONLY' })"
Write-Host ""

$rows = Import-Csv -LiteralPath $resolvedCsv -Delimiter $delimiter

if ($null -eq $rows) {
    throw "CSV file contains no data."
}

$headers = @($rows[0].PSObject.Properties.Name)

foreach ($required in @("Description", "Material", "Warehouse")) {
    if ($headers -notcontains $required) {
        throw "CSV is missing required '$required' column. Found: $($headers -join ', ')"
    }
}

$warehouseResponse = Invoke-RestMethod "$ApiBaseUrl/warehouses?includeInactive=true" -TimeoutSec 30

# Windows PowerShell can preserve a JSON array as one collection object.
# Flatten it explicitly so every warehouse becomes its own object.
$warehouses = New-Object System.Collections.Generic.List[object]
foreach ($candidate in @($warehouseResponse)) {
    foreach ($warehouse in @($candidate)) {
        if ($null -ne $warehouse -and -not [string]::IsNullOrWhiteSpace([string]$warehouse.code)) {
            $warehouses.Add($warehouse)
        }
    }
}

if ($warehouses.Count -eq 0) {
    throw "The API returned no warehouses."
}

$warehouseByCode = @{}
foreach ($warehouse in $warehouses) {
    $apiCode = ([string]$warehouse.code).Trim().ToUpperInvariant()
    $warehouseByCode[$apiCode] = $warehouse
}

Write-Host ""
Write-Host "API warehouse map:" -ForegroundColor DarkGray
foreach ($warehouse in ($warehouses | Sort-Object sortOrder)) {
    $apiCode = ([string]$warehouse.code).Trim().ToUpperInvariant()
    Write-Host ("  {0,-12} -> {1}" -f $apiCode, [string]$warehouse.name) -ForegroundColor DarkGray
}

Write-Host "API warehouse keys: $($warehouseByCode.Keys -join ', ')" -ForegroundColor DarkGray

$groups = @{}
$skippedRows = 0
$unknownWarehouses = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)

Write-Host ""
Write-Host "CSV warehouse mappings:" -ForegroundColor DarkGray
$rows |
    ForEach-Object { ([string]$_.Warehouse).Trim() } |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Sort-Object -Unique |
    ForEach-Object {
        $mappedCode = (Normalize-WarehouseCode $_).Trim().ToUpperInvariant()
        Write-Host ("  '{0}' -> {1}" -f $_, $mappedCode) -ForegroundColor DarkGray
    }

foreach ($row in $rows) {
    $material = ([string]$row.Material).Trim()
    $description = ([string]$row.Description).Trim()
    $warehouseCode = (Normalize-WarehouseCode ([string]$row.Warehouse)).Trim().ToUpperInvariant()

    if ([string]::IsNullOrWhiteSpace($material) -and [string]::IsNullOrWhiteSpace($description)) {
        $skippedRows++
        continue
    }

    if ([string]::IsNullOrWhiteSpace($material)) {
        Write-Warning "Skipping row with no Material number: '$description'"
        $skippedRows++
        continue
    }

    if ([string]::IsNullOrWhiteSpace($description)) {
        Write-Warning "Skipping material '$material' because Description is blank."
        $skippedRows++
        continue
    }

    if (-not $warehouseByCode.ContainsKey($warehouseCode)) {
        [void]$unknownWarehouses.Add(([string]$row.Warehouse).Trim())
        continue
    }

    $key = $material.ToUpperInvariant()

    if (-not $groups.ContainsKey($key)) {
        $groups[$key] = [pscustomobject]@{
            MaterialNumber = $material
            Description = $description
            WarehouseIds = New-Object System.Collections.Generic.HashSet[int]
            WarehouseNames = New-Object System.Collections.Generic.HashSet[string] ([System.StringComparer]::OrdinalIgnoreCase)
        }
    }

    $group = $groups[$key]

    if ($group.Description -ne $description) {
        Write-Warning "Material '$material' has multiple descriptions. Keeping '$($group.Description)' and ignoring '$description'."
    }

    $warehouse = $warehouseByCode[$warehouseCode]
    [void]$group.WarehouseIds.Add([int]$warehouse.id)
    [void]$group.WarehouseNames.Add([string]$warehouse.name)
}

if ($unknownWarehouses.Count -gt 0) {
    Write-Host ""
    Write-Host "Unknown warehouse values found in CSV:" -ForegroundColor Red
    $unknownWarehouses | Sort-Object | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-Host ""
    throw "Import stopped. No database changes were made. Add/match the warehouse values above before importing."
}

$catalog = @($groups.Values | Sort-Object Description, MaterialNumber)

Write-Host "CSV rows read       : $($rows.Count)"
Write-Host "Rows skipped        : $skippedRows"
Write-Host "Unique materials    : $($catalog.Count)"
Write-Host ""

foreach ($warehouse in $warehouses | Sort-Object sortOrder) {
    $count = @($catalog | Where-Object { $_.WarehouseIds.Contains([int]$warehouse.id) }).Count
    Write-Host ("{0,-24} {1,6} parts" -f $warehouse.name, $count)
}

$existing = @(Invoke-RestMethod "$ApiBaseUrl/parts?includeInactive=true" -TimeoutSec 30)
$existingByMaterial = @{}

foreach ($part in $existing) {
    $existingByMaterial[[string]$part.materialNumber.ToUpperInvariant()] = $part
}

$creates = 0
$updates = 0
$unchanged = 0

foreach ($item in $catalog) {
    $key = $item.MaterialNumber.ToUpperInvariant()
    $warehouseIds = @($item.WarehouseIds | Sort-Object)

    if (-not $existingByMaterial.ContainsKey($key)) {
        $creates++
        continue
    }

    $current = $existingByMaterial[$key]
    $currentIds = @($current.warehouses | ForEach-Object { [int]$_.id } | Sort-Object)

    $sameDescription = ([string]$current.description).Trim() -eq $item.Description
    $sameWarehouses = (@($currentIds) -join ",") -eq (@($warehouseIds) -join ",")

    if ($sameDescription -and $sameWarehouses -and [bool]$current.isActive) {
        $unchanged++
    }
    else {
        $updates++
    }
}

Write-Host ""
Write-Host "Planned changes" -ForegroundColor Cyan
Write-Host "Create    : $creates"
Write-Host "Update    : $updates"
Write-Host "Unchanged : $unchanged"

if (-not $Apply) {
    Write-Host ""
    Write-Host "PREVIEW COMPLETE - no database changes were made." -ForegroundColor Yellow
    Write-Host "If the counts look right, rerun with -Apply." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Applying import..." -ForegroundColor Cyan

$changedBy = $env:USERNAME
$createdCount = 0
$updatedCount = 0

foreach ($item in $catalog) {
    $key = $item.MaterialNumber.ToUpperInvariant()
    $warehouseIds = @($item.WarehouseIds | Sort-Object)

    $body = @{
        materialNumber = $item.MaterialNumber
        description = $item.Description
        warehouseIds = $warehouseIds
        changedBy = $changedBy
    }

    if (-not $existingByMaterial.ContainsKey($key)) {
        [void](Invoke-Api -Method "POST" -Uri "$ApiBaseUrl/parts" -Body $body)
        $createdCount++
        continue
    }

    $current = $existingByMaterial[$key]
    $currentIds = @($current.warehouses | ForEach-Object { [int]$_.id } | Sort-Object)
    $sameDescription = ([string]$current.description).Trim() -eq $item.Description
    $sameWarehouses = (@($currentIds) -join ",") -eq (@($warehouseIds) -join ",")

    if ($sameDescription -and $sameWarehouses -and [bool]$current.isActive) {
        continue
    }

    [void](Invoke-Api -Method "PUT" -Uri "$ApiBaseUrl/parts/$($current.id)" -Body $body)

    if (-not [bool]$current.isActive) {
        [void](Invoke-Api -Method "POST" -Uri "$ApiBaseUrl/parts/$($current.id)/reactivate?changedBy=$([uri]::EscapeDataString($changedBy))" -Body $null)
    }

    $updatedCount++
}

Write-Host ""
Write-Host "IMPORT COMPLETE" -ForegroundColor Green
Write-Host "Created : $createdCount" -ForegroundColor Green
Write-Host "Updated : $updatedCount" -ForegroundColor Green

$final = @(Invoke-RestMethod "$ApiBaseUrl/parts" -TimeoutSec 30)
Write-Host "Active parts now in database: $($final.Count)" -ForegroundColor Green
