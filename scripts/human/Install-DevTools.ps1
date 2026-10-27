<#
.SYNOPSIS
    Henicos (G#) 開発環境に必要なCLIツール・ランタイムを一括セットアップするPowerShellスクリプト。

.DESCRIPTION
    リポジトリ開発 (`justfile`, `dotnet`, `watchexec` 等) および
    ガイドライン (`gemini.md`) で規定された各種高速CLIツール (`fd`, `rg`, `jq`, `yq`, `sd`, `sg`, `uv` 等) を
    winget (Windows Package Manager) 経由で自律的に検出し、未導入のパッケージをインストールします。

.PARAMETER Category
    インストール対象のカテゴリ ("Core", "CLI", "Lang", "All")。既定は "All"。

.PARAMETER DryRun
    実際のインストールを行わず、現在のインストール状況と対象一覧のみを表示します。

.EXAMPLE
    pwsh ./scripts/human/Install-DevTools.ps1 -DryRun
    現在の環境でどのツールが未導入かを確認します。

.EXAMPLE
    pwsh ./scripts/human/Install-DevTools.ps1
    すべての未導入ツールをインストールします。
#>

[CmdletBinding()]
param (
    [ValidateSet("All", "Core", "CLI", "Lang")]
    [string]$Category = "All",

    [switch]$DryRun
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

# UTF-8 出力設定
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$OutputEncoding = [System.Text.Encoding]::UTF8

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " Henicos (G#) 開発環境ツールセットアップスクリプト" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. winget の存在確認
$wingetCmd = Get-Command "winget" -ErrorAction SilentlyContinue
if (-not $wingetCmd) {
    Write-Host "[ERROR] 'winget' (Windows Package Manager) が見つかりませんでした。" -ForegroundColor Red
    Write-Host "Microsoft Store から「アプリ インストーラー」を更新・インストールしてください。" -ForegroundColor Yellow
    exit 1
}

# 2. ツールマニフェスト定義
$tools = @(
    # --- コア開発ランタイム & ビルドツール ---
    [PSCustomObject]@{
        Id          = "Microsoft.PowerShell"
        Name        = "PowerShell 7+ (pwsh)"
        Command     = "pwsh"
        Category    = "Core"
        Description = "justfile 既定シェル / 自動化スクリプト実行"
    },
    [PSCustomObject]@{
        Id          = "Git.Git"
        Name        = "Git"
        Command     = "git"
        Category    = "Core"
        Description = "バージョン管理システム"
    },
    [PSCustomObject]@{
        Id          = "GitHub.cli"
        Name        = "GitHub CLI (gh)"
        Command     = "gh"
        Category    = "Core"
        Description = "Issue / PR 連携・ワークフロー自動化"
    },
    [PSCustomObject]@{
        Id          = "Microsoft.DotNet.SDK.10"
        Name        = ".NET SDK 10.0"
        Command     = "dotnet"
        Category    = "Core"
        Description = "C# / GSharp ビルド & テスト実行"
    },
    [PSCustomObject]@{
        Id          = "Casey.Just"
        Name        = "Just"
        Command     = "just"
        Category    = "Core"
        Description = "タスクランナー (justfile 実行用)"
    },
    [PSCustomObject]@{
        Id          = "watchexec.watchexec"
        Name        = "Watchexec"
        Command     = "watchexec"
        Category    = "Core"
        Description = "ファイル変更監視・自動テスト (just watch)"
    },
    [PSCustomObject]@{
        Id          = "OpenJS.NodeJS.LTS"
        Name        = "Node.js (LTS)"
        Command     = "node"
        Category    = "Core"
        Description = "Roadmap UI / npx ツール実行"
    },

    # --- 高速CLI & 構文操作ツール (gemini.md 規定) ---
    [PSCustomObject]@{
        Id          = "sharkdp.fd"
        Name        = "fd"
        Command     = "fd"
        Category    = "CLI"
        Description = "高速ファイル・ディレクトリ検索"
    },
    [PSCustomObject]@{
        Id          = "BurntSushi.ripgrep.MSVC"
        Name        = "ripgrep (rg)"
        Command     = "rg"
        Category    = "CLI"
        Description = "高速テキスト・パターン検索"
    },
    [PSCustomObject]@{
        Id          = "jqlang.jq"
        Name        = "jq"
        Command     = "jq"
        Category    = "CLI"
        Description = "JSON データ抽出・加工"
    },
    [PSCustomObject]@{
        Id          = "MikeFarah.yq"
        Name        = "yq"
        Command     = "yq"
        Category    = "CLI"
        Description = "YAML / XML / JSON 構造化データ編集"
    },
    [PSCustomObject]@{
        Id          = "chmln.sd"
        Name        = "sd"
        Command     = "sd"
        Category    = "CLI"
        Description = "正規表現による高速文字列置換"
    },
    [PSCustomObject]@{
        Id          = "ast-grep.ast-grep"
        Name        = "ast-grep (sg)"
        Command     = "sg"
        Category    = "CLI"
        Description = "構文木 (AST) ベースのコード検索・置換"
    },
    [PSCustomObject]@{
        Id          = "Microsoft.Coreutils"
        Name        = "GNU Coreutils"
        Command     = "cat.exe"
        Category    = "CLI"
        Description = "cat.exe, head.exe, wc.exe などの標準コマンド"
    },

    # --- スクリプト & 補助ランタイム ---
    [PSCustomObject]@{
        Id          = "astral-sh.uv"
        Name        = "uv"
        Command     = "uv"
        Category    = "Lang"
        Description = "高速 Python パッケージ・仮想環境管理"
    },
    [PSCustomObject]@{
        Id          = "Python.Python.3.12"
        Name        = "Python 3.12"
        Command     = "python"
        Category    = "Lang"
        Description = "データ処理・汎用スクリプト実行"
    },
    [PSCustomObject]@{
        Id          = "PHP.PHP.8.3"
        Name        = "PHP 8.3"
        Command     = "php"
        Category    = "Lang"
        Description = "CLI スクリプト実行環境"
    }
)

# カテゴリ絞り込み
$targetTools = if ($Category -eq "All") {
    $tools
} else {
    $tools | Where-Object { $_.Category -eq $Category }
}

Write-Host "インストール対象カテゴリ: $Category (計 $($targetTools.Count) ツール)" -ForegroundColor Gray
Write-Host "現在のインストール状況をチェック中..." -ForegroundColor Yellow

$toInstall = [System.Collections.Generic.List[PSCustomObject]]::new()
$alreadyInstalled = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($tool in $targetTools) {
    # 1. PATH 上にコマンドが存在するかチェック (cargo, 手動配置, winget いずれも対応)
    $cmdExists = [bool](Get-Command $tool.Command -ErrorAction SilentlyContinue)

    # 2. winget list でパッケージとして登録されているかチェック
    $isWingetInstalled = $false
    if (-not $cmdExists) {
        $listOutput = (winget list --id $tool.Id --exact 2>$null | Out-String)
        $isWingetInstalled = ($LASTEXITCODE -eq 0 -and $listOutput -match [regex]::Escape($tool.Id))
    }

    if ($cmdExists -or $isWingetInstalled) {
        $alreadyInstalled.Add($tool)
        $detail = if ($cmdExists) { "コマンド検出: $($tool.Command)" } else { "winget 検出: $($tool.Id)" }
        Write-Host " [OK] 導入済み: $($tool.Name.PadRight(22)) ($detail)" -ForegroundColor Green
    } else {
        $toInstall.Add($tool)
        Write-Host " [-] 未導入  : $($tool.Name.PadRight(22)) (コマンド: $($tool.Command) / ID: $($tool.Id))" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "----------------------------------------------------------" -ForegroundColor Gray
Write-Host " 導入済み: $($alreadyInstalled.Count) 件 / 未導入: $($toInstall.Count) 件" -ForegroundColor Cyan
Write-Host "----------------------------------------------------------" -ForegroundColor Gray

if ($toInstall.Count -eq 0) {
    Write-Host "すべての対象ツールが既に導入されています。セットアップ完了です！" -ForegroundColor Green
    exit 0
}

if ($DryRun) {
    Write-Host "[DryRun モード] 実際のインストールはスキップされました。" -ForegroundColor Yellow
    Write-Host "インストールを実行するには、-DryRun を外して実行してください:" -ForegroundColor Gray
    Write-Host "  pwsh ./scripts/human/Install-DevTools.ps1" -ForegroundColor White
    exit 0
}

# インストール実行
Write-Host ""
Write-Host "未導入のツール ($($toInstall.Count) 件) のインストールを開始します..." -ForegroundColor Cyan

$failed = [System.Collections.Generic.List[PSCustomObject]]::new()

foreach ($item in $toInstall) {
    Write-Host "`n>>> インストール中: $($item.Name) ($($item.Id))..." -ForegroundColor Cyan
    Write-Host "    用途: $($item.Description)" -ForegroundColor Gray

    # winget install 実行
    $process = Start-Process -FilePath "winget" `
        -ArgumentList @("install", "--id", $item.Id, "--exact", "--source", "winget", "--accept-package-agreements", "--accept-source-agreements") `
        -NoNewWindow -Wait -PassThru

    if ($process.ExitCode -eq 0) {
        Write-Host ">>> 完了: $($item.Name)" -ForegroundColor Green
    } else {
        Write-Host ">>> [WARN] インストール失敗または要手動対応 (ExitCode: $($process.ExitCode)): $($item.Name)" -ForegroundColor Red
        $failed.Add($item)
    }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " セットアップ結果サマリー" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " 成功: $($toInstall.Count - $failed.Count) 件" -ForegroundColor Green
if ($failed.Count -gt 0) {
    Write-Host " 失敗: $($failed.Count) 件" -ForegroundColor Red
    foreach ($f in $failed) {
        Write-Host "   - $($f.Name) ($($f.Id))" -ForegroundColor Red
    }
    Write-Host "`n管理者権限の PowerShell で再実行するか、個別インストールをお試しください。" -ForegroundColor Yellow
} else {
    Write-Host "すべてのツールのセットアップが正常に完了しました。" -ForegroundColor Green
}

Write-Host "`n[重要] インストールされたCLIツールの PATH を反映するため、" -ForegroundColor Yellow
Write-Host "ターミナル（PowerShell / VS Code）を再起動してください。" -ForegroundColor Yellow
Write-Host "==========================================================" -ForegroundColor Cyan
