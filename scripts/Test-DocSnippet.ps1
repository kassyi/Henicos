param(
    [Parameter(Mandatory=$true)]
    [string]$FilePath,
    
    [Parameter(Mandatory=$false)]
    [string]$Assemblies = ""
)

$ErrorActionPreference = "Stop"

# Paths
$RepoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$DocTestProj = Join-Path $RepoRoot "tests\Compiler\Kassyi.GSharp.DocTest\Kassyi.GSharp.DocTest.csproj"
$ProgramCs = Join-Path $RepoRoot "tests\Compiler\Kassyi.GSharp.DocTest\Program.cs"

if (-not (Test-Path $FilePath)) {
    Write-Error "Snippet file not found: $FilePath"
    exit 1
}

# 1. Clean the project
$xml = [xml](Get-Content $DocTestProj)
$itemGroups = $xml.Project.ItemGroup | Where-Object { $_.ProjectReference }
if ($itemGroups) {
    $groupsArray = @($itemGroups)
    foreach ($group in $groupsArray) {
        $xml.Project.RemoveChild($group) | Out-Null
    }
    $xml.Save($DocTestProj)
}

# 2. Add References
if (-not [string]::IsNullOrWhiteSpace($Assemblies)) {
    $asmList = $Assemblies.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ -ne "" }
    foreach ($asm in $asmList) {
        $projPath = fd -p "$asm.csproj$" "src"
        if (-not [string]::IsNullOrWhiteSpace($projPath)) {
            $firstProj = ($projPath -split "`n" | Select-Object -First 1).Trim()
            dotnet add $DocTestProj reference $firstProj | Out-Null
        } else {
            Write-Error "Could not find project for assembly: $asm"
            exit 1
        }
    }
}

# 3. Copy or Extract Code
if ($FilePath.EndsWith(".md")) {
    $lines = Get-Content $FilePath
    $inExamples = $false
    $inCodeBlock = $false
    $codeLines = @()
    foreach ($line in $lines) {
        if ($line.Trim() -match '^## Examples') {
            $inExamples = $true
            continue
        }
        if ($inExamples) {
            if ($line.Trim() -match '^```csharp') {
                $inCodeBlock = $true
                continue
            }
            if ($inCodeBlock) {
                if ($line.Trim() -match '^```$') {
                    break
                }
                $codeLines += $line
            }
        }
    }
    
    if ($codeLines.Count -eq 0) {
        Write-Host "DEBUG: Total lines read = $($lines.Count)"
        Write-Host "DEBUG: Did it find Examples? $inExamples"
        Write-Host "DEBUG: Did it find code block? $inCodeBlock"
        Write-Error "No csharp code block found under '## Examples' in the markdown file."
        exit 1
    }
    Set-Content -Path $ProgramCs -Value $codeLines -Encoding UTF8
} else {
    Copy-Item -Path $FilePath -Destination $ProgramCs -Force
}

# 4. Build & Run
dotnet run --project $DocTestProj
$exitCode = $LASTEXITCODE

if ($exitCode -ne 0) {
    exit $exitCode
} else {
    Write-Host "DocTest SUCCESS. The assembly dependencies are proven." -ForegroundColor Green
}
