<#
.SYNOPSIS
    Demangles generated G# C++/CUDA identifiers (__gsharp_*) in compiler or runtime logs back to C# symbol names.

.DESCRIPTION
    Replaces mangled G# identifiers (__gsharp_k_*, __gsharp_fn_*, __gsharp_ty_*, __gsharp_id_*, __gsharp_mb_*, __gsharp_tp_*,
    including hash-shortened _h<10 hex> identifiers) with their original C# names.
    Automatically discovers *.gsym.json symbol map files under obj/, src/, and tests/ when -SymbolsPath is omitted,
    verifies companion .g.cu / .g.cpp '// gsharp-symbols: <hash>' headers when present, and delegates to GSharpNameEncoding
    so CLI decoding matches the compiler 1:1.

.PARAMETER LogPath
    Path to the log file to demangle, '-' to read from standard input (default), or 'help' / '--help' / '-h' to display usage.

.PARAMETER SymbolsPath
    Optional path to a specific *.gsym.json file, a companion *.g.cu / *.g.cpp artifact, or a directory containing *.gsym.json files.
    When omitted, *.gsym.json files are discovered recursively under ./obj, ./src, and ./tests.

.PARAMETER Help
    Displays usage and examples and exits with code 0.

.PARAMETER InputObject
    Lines piped directly from the PowerShell pipeline.

.EXAMPLE
    just gsharp-demangle build.log
    # Demangles build.log using auto-discovered *.gsym.json symbol files.

.EXAMPLE
    dotnet build 2>&1 | pwsh ./scripts/Demangle-GSharpLog.ps1
    # Demangles compiler output piped via standard input.

.EXAMPLE
    pwsh ./scripts/Demangle-GSharpLog.ps1 -LogPath nvcc.log -SymbolsPath tests/Compiler/Kassyi.GSharp.Tests.Cuda/obj
    # Demangles nvcc.log using symbol files from a specific obj/ directory.

.EXAMPLE
    pwsh ./scripts/Demangle-GSharpLog.ps1 --help
    # Prints usage information.
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$LogPath = '-',

    [Parameter(Position = 1)]
    [string]$SymbolsPath = '',

    [Parameter()]
    [Alias('h')]
    [switch]$Help,

    [Parameter(ValueFromPipeline = $true)]
    [AllowNull()]
    [AllowEmptyString()]
    [string[]]$InputObject
)

begin {
    Set-StrictMode -Version Latest
    $ErrorActionPreference = 'Stop'
    [Console]::OutputEncoding = [System.Text.Encoding]::UTF8

    function Show-Usage {
        @"
Usage:
  pwsh ./scripts/Demangle-GSharpLog.ps1 [-LogPath <file|-|help>] [-SymbolsPath <file|dir>] [-Help]
  <command> | pwsh ./scripts/Demangle-GSharpLog.ps1 [-SymbolsPath <file|dir>]
  just gsharp-demangle <log_path|-> [symbols_path]
  just gsharp-demangle help

Description:
  Replaces mangled G# C++/CUDA identifiers (__gsharp_k_*, __gsharp_fn_*, __gsharp_ty_*,
  __gsharp_id_*, __gsharp_mb_*, __gsharp_tp_*, and shortened _h<hex> names) with their
  original C# names using *.gsym.json symbol maps and GSharpNameEncoding rules.

Arguments & Options:
  -LogPath <path|->      Path to the log file to demangle, or '-' for stdin (default: '-').
  -SymbolsPath <path>    Optional path to a *.gsym.json file, a companion *.g.cu/*.g.cpp file,
                         or a directory to search. Defaults to recursive discovery under
                         ./obj, ./src, and ./tests.
  -Help, -h, --help      Show this help message and exit.

Examples:
  just gsharp-demangle build.log
  just gsharp-demangle build.log tests/Compiler/Kassyi.GSharp.Tests.Cuda/obj
  dotnet build 2>&1 | pwsh ./scripts/Demangle-GSharpLog.ps1
  Get-Content nvcc.err | pwsh ./scripts/Demangle-GSharpLog.ps1 -LogPath -
"@
    }

    $showHelp = $Help.IsPresent -or ($LogPath -in @('--help', '-h', '-?', '/?', 'help')) -or ($SymbolsPath -in @('--help', '-h', '-?', '/?'))
    if ($showHelp) {
        Show-Usage
        return
    }

    if (-not ('Kassyi.GSharp.Naming.GSharpDemanglerCli' -as [type])) {
        $repoRoot = Split-Path -Parent $PSScriptRoot
        $encodingFile = Join-Path $repoRoot 'src/Shared/Naming/GSharpNameEncoding.cs'
        if (-not (Test-Path -LiteralPath $encodingFile -PathType Leaf)) {
            throw "GSharpNameEncoding.cs not found at: $encodingFile"
        }

        $src = [System.IO.File]::ReadAllText($encodingFile, [System.Text.Encoding]::UTF8)
        $header = "#nullable enable`nusing System;`nusing System.Collections.Generic;`n"
        $footer = @"
public static class GSharpDemanglerCli {
    public static IReadOnlyDictionary<string, string> Parse(string gsym, string? expectedHash) =>
        GSharpNameEncoding.ParseSymbolFile(gsym, string.IsNullOrEmpty(expectedHash) ? null : expectedHash);
    public static bool TryReadHash(string src, out string hash) =>
        GSharpNameEncoding.TryReadSymbolHeaderHash(src, out hash);
    public static string Decode(string text, IReadOnlyDictionary<string, string>? map) =>
        GSharpNameEncoding.DecodeText(text, map);
}
"@
        Add-Type -TypeDefinition ($header + $src + "`n" + $footer) -Language CSharp
    }

    function Resolve-SymbolMap {
        param([string]$ExplicitPath)

        $map = [System.Collections.Generic.Dictionary[string, string]]::new([System.StringComparer]::Ordinal)
        $gsymFiles = [System.Collections.Generic.List[string]]::new()

        if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
            if (-not (Test-Path -LiteralPath $ExplicitPath)) {
                throw "Symbols path not found: $ExplicitPath"
            }

            $item = Get-Item -LiteralPath $ExplicitPath
            if ($item.PSIsContainer) {
                foreach ($f in [System.IO.Directory]::EnumerateFiles($item.FullName, '*.gsym.json', [System.IO.SearchOption]::AllDirectories)) {
                    [void]$gsymFiles.Add($f)
                }
            }
            elseif ($ExplicitPath.EndsWith('.gsym.json', [System.StringComparison]::OrdinalIgnoreCase)) {
                [void]$gsymFiles.Add($item.FullName)
            }
            elseif (Test-Path -LiteralPath "$($item.FullName).gsym.json" -PathType Leaf) {
                [void]$gsymFiles.Add("$($item.FullName).gsym.json")
            }
            else {
                throw "Unsupported symbols path or missing companion .gsym.json: $ExplicitPath"
            }
        }
        else {
            $cwd = (Get-Location).ProviderPath
            foreach ($rootName in @('obj', 'src', 'tests')) {
                $candidateDir = Join-Path $cwd $rootName
                if ([System.IO.Directory]::Exists($candidateDir)) {
                    foreach ($f in [System.IO.Directory]::EnumerateFiles($candidateDir, '*.gsym.json', [System.IO.SearchOption]::AllDirectories)) {
                        [void]$gsymFiles.Add($f)
                    }
                }
            }
        }

        foreach ($gsymFile in $gsymFiles) {
            $expectedHash = $null
            $sourceFile = $gsymFile -replace '\.gsym\.json$', ''
            if ($sourceFile -ne $gsymFile -and [System.IO.File]::Exists($sourceFile)) {
                $firstLine = $null
                $reader = [System.IO.File]::OpenText($sourceFile)
                try {
                    $firstLine = $reader.ReadLine()
                }
                finally {
                    $reader.Dispose()
                }
                if ($null -ne $firstLine) {
                    $hash = ''
                    if ([Kassyi.GSharp.Naming.GSharpDemanglerCli]::TryReadHash($firstLine, [ref]$hash)) {
                        $expectedHash = $hash
                    }
                }
            }

            $gsymContent = [System.IO.File]::ReadAllText($gsymFile, [System.Text.Encoding]::UTF8)
            if (-not [string]::IsNullOrEmpty($gsymContent)) {
                $parsed = [Kassyi.GSharp.Naming.GSharpDemanglerCli]::Parse($gsymContent, $expectedHash)
                foreach ($kv in $parsed.GetEnumerator()) {
                    $map[$kv.Key] = $kv.Value
                }
            }
        }

        return $map
    }

    $symbolMap = Resolve-SymbolMap -ExplicitPath $SymbolsPath
    $hasPipelineInput = $false
}

process {
    if ($showHelp) {
        return
    }

    if ($PSBoundParameters.ContainsKey('InputObject') -or ($null -ne $_)) {
        $hasPipelineInput = $true
        foreach ($line in $InputObject) {
            if ($null -ne $line) {
                [Kassyi.GSharp.Naming.GSharpDemanglerCli]::Decode($line, $symbolMap)
            }
        }
    }
}

end {
    if ($showHelp -or $hasPipelineInput) {
        return
    }

    if ([string]::IsNullOrWhiteSpace($LogPath) -or $LogPath -eq '-') {
        if ([Console]::IsInputRedirected) {
            while ($null -ne ($line = [Console]::In.ReadLine())) {
                [Kassyi.GSharp.Naming.GSharpDemanglerCli]::Decode($line, $symbolMap)
            }
        }
        else {
            Show-Usage
        }
        return
    }

    if (-not (Test-Path -LiteralPath $LogPath -PathType Leaf)) {
        throw "Log file not found: $LogPath"
    }

    foreach ($line in [System.IO.File]::ReadLines((Resolve-Path -LiteralPath $LogPath).ProviderPath, [System.Text.Encoding]::UTF8)) {
        [Kassyi.GSharp.Naming.GSharpDemanglerCli]::Decode($line, $symbolMap)
    }
}
