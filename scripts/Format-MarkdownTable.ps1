<#
.SYNOPSIS
    Markdown 内のテーブルをコンパクト形式（不要なパディングを除去し、最小幅に正規化）に一括整形します。

.DESCRIPTION
    指定されたファイルまたはディレクトリ配下の *.md ファイルを走査し、
    コードブロック（```）外の Markdown テーブルに対して以下を適用します：
    1. 各セル内の余分な連続スペースを Trim して '| a | b |' 形式にコンパクト化
    2. アラインメント区切り行（:---, :---:, ---:, ---）の余分なハイフンを最小化
    3. 改行コード（LF）と UTF-8 (BOMなし) を維持して保存

.PARAMETER Path
    対象の Markdown ファイル、またはディレクトリ（デフォルト: 'docs/spec'）

.EXAMPLE
    ./scripts/Format-MarkdownTable.ps1
    ./scripts/Format-MarkdownTable.ps1 -Path docs/spec/en/03_bcl_api/diagnostics/GSH0001.md
#>
[CmdletBinding()]
param(
    [Parameter(Position = 0, ValueFromPipeline = $true)]
    [string]$Path = 'docs/spec'
)

[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)

if (-not (Test-Path -Path $Path)) {
    Write-Warning "Target path '$Path' does not exist."
    return
}

$files = if ((Get-Item -Path $Path) -is [System.IO.DirectoryInfo]) {
    Get-ChildItem -Path $Path -Filter *.md -Recurse -File
} else {
    Get-Item -Path $Path
}

$totalModified = 0

foreach ($file in $files) {
    $content = [System.IO.File]::ReadAllText($file.FullName, [System.Text.Encoding]::UTF8)
    $lines = $content -split '\r?\n'
    $newLines = [System.Collections.Generic.List[string]]::new()
    $inCodeBlock = $false
    $modified = $false

    foreach ($line in $lines) {
        $trimmed = $line.Trim()

        # Toggle code block state (``` or ~~~)
        if ($trimmed -match '^(`{3,}|~{3,})') {
            $inCodeBlock = -not $inCodeBlock
            $newLines.Add($line)
            continue
        }
        if ($inCodeBlock) {
            $newLines.Add($line)
            continue
        }

        # Check if line is a markdown table row (| ... |)
        if ($trimmed.StartsWith('|') -and $trimmed.EndsWith('|') -and $trimmed.Length -gt 1) {
            $rawCells = [System.Text.RegularExpressions.Regex]::Split($trimmed, '(?<!\\)\|')
            if ($rawCells.Length -ge 3 -and $rawCells[0] -eq '' -and $rawCells[-1] -eq '') {
                $cells = [System.Collections.Generic.List[string]]::new()
                for ($i = 1; $i -lt ($rawCells.Length - 1); $i++) {
                    $cells.Add($rawCells[$i].Trim())
                }

                # Determine whether this is a delimiter row
                $isDelimiter = $true
                foreach ($c in $cells) {
                    if ($c -notmatch '^(:?-+:?|)$') {
                        $isDelimiter = $false
                        break
                    }
                }
                if ($cells.Count -eq 0 -or ($cells -join '') -eq '') {
                    $isDelimiter = $false
                }

                if ($isDelimiter) {
                    $norm = [System.Collections.Generic.List[string]]::new()
                    foreach ($c in $cells) {
                        if ($c -match '^:-+:$') { $norm.Add(':---:') }
                        elseif ($c -match '^:-+$') { $norm.Add(':---') }
                        elseif ($c -match '^-+:$') { $norm.Add('---:') }
                        elseif ($c -match '^-+$') { $norm.Add('---') }
                        else { $norm.Add($c) }
                    }
                    $formatted = '| ' + ($norm -join ' | ') + ' |'
                } else {
                    $formatted = '| ' + ($cells -join ' | ') + ' |'
                }

                if ($formatted -ne $line) {
                    $modified = $true
                }
                $newLines.Add($formatted)
                continue
            }
        }

        $newLines.Add($line)
    }

    if ($modified) {
        [System.IO.File]::WriteAllText($file.FullName, ($newLines -join "`n"), $utf8NoBom)
        Write-Host "Compacted: $($file.FullName)" -ForegroundColor Green
        $totalModified++
    }
}

Write-Host "Finished table compaction. Modified $totalModified file(s)." -ForegroundColor Cyan
