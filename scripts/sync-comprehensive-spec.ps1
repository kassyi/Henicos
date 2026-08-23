param (
    [string]$SpecFilePath = $(Join-Path $PSScriptRoot "..\docs\spec\en\09_comprehensive_example.md"),
    [string]$SourceFile = $(Join-Path $PSScriptRoot "..\tests\Kassyi.GSharp.Tests\TestCases\ComprehensiveSpecKernel.cs"),
    [string]$GeneratedFile = $(Join-Path $PSScriptRoot "..\tests\Kassyi.GSharp.Tests\Snapshots\GeneratorSnapshotTests.ComprehensiveSpecKernel_GeneratesCorrectCudaSource#ComprehensiveSpecKernel_ProcessPhysics_Wrapper.g.verified.cs")
)

Write-Host "Syncing comprehensive spec..."

if (-not (Test-Path $SourceFile)) {
    Write-Error "Source file not found: $SourceFile"
    exit 1
}

if (-not (Test-Path $GeneratedFile)) {
    Write-Error "Generated file not found: $GeneratedFile"
    exit 1
}

$sourceCode = Get-Content $SourceFile -Raw
$sourceCode = $sourceCode.TrimEnd()

$generatedCode = Get-Content $GeneratedFile -Raw
$generatedCode = $generatedCode.TrimEnd()

$specContent = Get-Content $SpecFilePath -Raw

function Update-SectionRaw($content, $beginMarker, $endMarker, $language, $newCode) {
    $beginIdx = $content.IndexOf($beginMarker)
    $endIdx = $content.IndexOf($endMarker)
    
    if ($beginIdx -ge 0 -and $endIdx -gt $beginIdx) {
        $prefix = $content.Substring(0, $beginIdx + $beginMarker.Length)
        $suffix = $content.Substring($endIdx)
        $nl = [Environment]::NewLine
        return $prefix + $nl + '```' + $language + $nl + $newCode + $nl + '```' + $nl + $suffix
    }
    return $content
}

$specContent = Update-SectionRaw $specContent "<!-- BEGIN CSHARP -->" "<!-- END CSHARP -->" "csharp" $sourceCode
$specContent = Update-SectionRaw $specContent "<!-- BEGIN CPP -->" "<!-- END CPP -->" "cpp" $generatedCode

[System.IO.File]::WriteAllText($SpecFilePath, $specContent)

Write-Host "Sync complete!"
