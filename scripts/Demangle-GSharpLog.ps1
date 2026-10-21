[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$LogPath,

    [Parameter(Position = 1)]
    [string]$SymbolsPath
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Read-LogLines {
    param([string]$Path)
    if ($Path -eq '-') {
        return @($input)
    }
    if (-not (Test-Path -Path $Path -PathType Leaf)) {
        throw "Log file not found: $Path"
    }
    return Get-Content -Path $Path
}

function Find-SymbolFiles {
    param([string]$ExplicitPath)

    if ($ExplicitPath) {
        if (-not (Test-Path -Path $ExplicitPath)) {
            throw "Symbols path not found: $ExplicitPath"
        }
        if ((Get-Item -Path $ExplicitPath).PSIsContainer) {
            return Get-ChildItem -Path $ExplicitPath -Filter "*.gsym.json" -Recurse | Select-Object -ExpandProperty FullName
        }
        return @($ExplicitPath)
    }

    $candidates = @()
    if (Test-Path -Path "obj") {
        $candidates += Get-ChildItem -Path "obj" -Filter "*.gsym.json" -Recurse | Select-Object -ExpandProperty FullName
    }
    return $candidates
}

function Parse-GsymFile {
    param([string]$FilePath)

    $lines = Get-Content -Path $FilePath
    $map = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
    $headerHash = $null

    foreach ($line in $lines) {
        $trimmed = $line.Trim()
        if ($trimmed.Length -eq 0) { continue }

        if ($headerHash -eq $null -and $trimmed -match '"gsharp_symbols"\s*:\s*"([^"]+)"') {
            $headerHash = $Matches[1]
            continue
        }

        $cpp = $null
        $cs = $null
        if ($trimmed -match '"cpp"\s*:\s*"([^"]+)"') {
            $cpp = $Matches[1]
        }
        if ($trimmed -match '"cs"\s*:\s*"([^"]+)"') {
            $cs = $Matches[1]
        }

        if ($cpp -and $cs) {
            # Unescape basic json escapes
            $cpp = $cpp -replace '\\\\', '\' -replace '\\"', '"'
            $cs = $cs -replace '\\\\', '\' -replace '\\"', '"'
            $map[$cpp] = $cs
        }
    }

    # If a companion .g.cu or .g.cpp file exists alongside the .gsym.json, check hash
    $cuFile = $FilePath -replace '\.gsym\.json$', ''
    if ($cuFile -ne $FilePath -and (Test-Path -Path $cuFile -PathType Leaf)) {
        $firstLine = Get-Content -Path $cuFile -First 1
        if ($firstLine -and $firstLine.StartsWith("// gsharp-symbols: ")) {
            $sourceHash = $firstLine.Substring("// gsharp-symbols: ".Length).Trim()
            if ($headerHash -and $sourceHash -and $headerHash -ne $sourceHash) {
                Write-Warning "Symbol file '$FilePath' hash '$headerHash' does not match source hash '$sourceHash' in '$cuFile'."
            }
        }
    }

    return $map
}

function Decode-GSharpIdentifier {
    param(
        [string]$Identifier,
        [System.Collections.Generic.Dictionary[string, string]]$SymbolMap
    )

    if ($SymbolMap -and $SymbolMap.ContainsKey($Identifier)) {
        return $SymbolMap[$Identifier]
    }

    if ($Identifier.StartsWith("__gsharp_mb_")) {
        return $Identifier.Substring("__gsharp_mb_".Length)
    }

    if ($Identifier.StartsWith("__gsharp_tp_")) {
        return $Identifier.Substring("__gsharp_tp_".Length)
    }

    if ($Identifier.StartsWith("__gsharp_id_")) {
        $rest = $Identifier.Substring("__gsharp_id_".Length)
        if ($rest -match '^\d+_(.+)$') {
            return $Matches[1]
        }
        return $rest
    }

    return $Identifier
}

function Demangle-Line {
    param(
        [string]$Line,
        [System.Collections.Generic.Dictionary[string, string]]$SymbolMap
    )

    $regex = [regex]'\b__gsharp_[A-Za-z0-9_]+\b'
    return $regex.Replace($Line, {
        param($match)
        return Decode-GSharpIdentifier -Identifier $match.Value -SymbolMap $SymbolMap
    })
}

$symbolMap = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
$gsymFiles = Find-SymbolFiles -ExplicitPath $SymbolsPath

foreach ($file in $gsymFiles) {
    $parsed = Parse-GsymFile -FilePath $file
    foreach ($kvp in $parsed.GetEnumerator()) {
        $symbolMap[$kvp.Key] = $kvp.Value
    }
}

$logLines = Read-LogLines -Path $LogPath
foreach ($line in $logLines) {
    Demangle-Line -Line $line -SymbolMap $symbolMap
}
