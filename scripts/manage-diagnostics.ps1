param (
    [switch]$CheckGaps,
    [switch]$NextId,
    [switch]$CheckDocs,
    [string]$JsonPath = "src\Compiler\Kassyi.GSharp.Compiler\Diagnostics\diagnostics.json",
    [string]$DocsDir = "docs\spec\en\03_bcl_api\diagnostics"
)

# Resolve paths
$basePath = (Get-Item $PSScriptRoot).Parent.FullName
$fullJsonPath = Join-Path $basePath $JsonPath
$fullDocsDir = Join-Path $basePath $DocsDir

if (-not (Test-Path $fullJsonPath)) {
    Write-Error "Could not find diagnostics.json at $fullJsonPath"
    exit 1
}

$diagnostics = Get-Content $fullJsonPath | ConvertFrom-Json

# Extract numbers
$numbers = $diagnostics | ForEach-Object { 
    if ($_.id -match 'GSH(\d+)') {
        [int]$matches[1]
    }
} | Sort-Object

if ($numbers.Count -eq 0) {
    Write-Error "No valid GSHxxxx IDs found."
    exit 1
}

$max = $numbers[-1]
$min = 1

if ($NextId) {
    $next = $max + 1
    Write-Output "Next available ID: GSH$("{0:D4}" -f $next)"
}

if ($CheckGaps) {
    $gaps = @()
    for ($i = $min; $i -lt $max; $i++) {
        if ($numbers -notcontains $i) {
            $gaps += $i
        }
    }
    if ($gaps.Count -gt 0) {
        Write-Warning "Found gaps in diagnostic IDs:"
        foreach ($gap in $gaps) {
            Write-Warning " - GSH$("{0:D4}" -f $gap) is missing."
        }
    } else {
        Write-Host "No gaps found in diagnostic IDs." -ForegroundColor Green
    }
}

if ($CheckDocs) {
    $missingDocs = @()
    foreach ($diag in $diagnostics) {
        $docPath = Join-Path $fullDocsDir "$($diag.id).md"
        if (-not (Test-Path $docPath)) {
            $missingDocs += $diag.id
        }
    }
    
    if ($missingDocs.Count -gt 0) {
        Write-Error "The following diagnostics are missing documentation in ${DocsDir}:"
        foreach ($id in $missingDocs) {
            Write-Error " - $id"
        }
        exit 1
    } else {
        Write-Host "All diagnostics have corresponding documentation." -ForegroundColor Green
    }
}

if (-not $CheckGaps -and -not $NextId -and -not $CheckDocs) {
    Write-Host "Usage: manage-diagnostics.ps1 [-CheckGaps] [-NextId] [-CheckDocs]"
}
