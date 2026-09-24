param(
    [Parameter(Mandatory=$true)]
    [string]$FilePath,
    
    [Parameter(Mandatory=$false)]
    [string]$Assemblies = ""
)

$ErrorActionPreference = "Stop"

# Paths
$RepoRoot = (Get-Item $PSScriptRoot).Parent.FullName
$DocTestProj = Join-Path $RepoRoot "tests\Kassyi.GSharp.DocTest\Kassyi.GSharp.DocTest.csproj"
$ProgramCs = Join-Path $RepoRoot "tests\Kassyi.GSharp.DocTest\Program.cs"

if (-not (Test-Path $FilePath)) {
    Write-Error "Snippet file not found: $FilePath"
    exit 1
}

# 1. Clean the project (remove all ItemGroups containing ProjectReference)
Write-Host "Resetting DocTest project dependencies..."
$xml = [xml](Get-Content $DocTestProj)
$itemGroups = $xml.Project.ItemGroup | Where-Object { $_.ProjectReference }
if ($itemGroups) {
    # Need to cast to array to avoid modifying collection while iterating
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
        Write-Host "Searching for project: $asm"
        # Search for csproj files in src/ that match the assembly name
        $projPath = fd -p "$asm.csproj$" "src"
        
        if (-not [string]::IsNullOrWhiteSpace($projPath)) {
            $firstProj = ($projPath -split "`n" | Select-Object -First 1).Trim()
            Write-Host "Adding reference: $firstProj"
            dotnet add $DocTestProj reference $firstProj | Out-Null
        } else {
            Write-Error "Could not find project for assembly: $asm"
            exit 1
        }
    }
}

# 3. Copy Code
Write-Host "Injecting snippet into Program.cs..."
Copy-Item -Path $FilePath -Destination $ProgramCs -Force

# 4. Build & Run
Write-Host "Compiling and running snippet..."
Write-Host "------------------------------------------------------------"
dotnet run --project $DocTestProj
$exitCode = $LASTEXITCODE
Write-Host "------------------------------------------------------------"

if ($exitCode -ne 0) {
    Write-Host "DocTest FAILED." -ForegroundColor Red
    exit $exitCode
} else {
    Write-Host "DocTest SUCCESS. The assembly dependencies are proven." -ForegroundColor Green
}
