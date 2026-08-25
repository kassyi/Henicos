#!/usr/bin/env node

/**
 * @file calculate_priorities.js
 * @description Henicos (G#) 開発タスクの優先度スコア計算・Tier分類・ロードマップ自動同期スクリプト
 */

const fs = require('fs');
const path = require('path');

// =============================================================================
// 定数定義 (Constants & Configuration)
// =============================================================================

const WEIGHTS = {
    BLOCKER: 4.0,   // B: 他タスクへの依存・ブロッキング度
    EASE: 3.0,      // E: 実装の容易さ (1 / 難易度)
    GENERALITY: 2.0 // G: 汎用性・出現頻度
};

const TIER_THRESHOLDS = {
    TIER_1: 36, // >= 36: 最優先 (即効性ブロッカー解放)
    TIER_2: 28, // >= 28: 準優先 (高難度インフラ / 早期着手)
    TIER_3: 20  // >= 20: 並行・隙間タスク
    // < 20: Tier 4 (後回し・ニッチ領域)
};

// =============================================================================
// タスク定義マスターデータ (Task Master Data)
// =============================================================================

const TASKS = [
    // --- GBI (GpuBigInt) 領域 ---
    { Id: "GBI-01", Title: "GSharpJitProvider メモリプールの実装", B: 3, E: 2, G: 5 },
    { Id: "GBI-02", Title: "Test_ModPow_Correctness の Skip 解除と検証", B: 2, E: 5, G: 2 },
    { Id: "GBI-03", Title: "CPU-Fallback メソッドの GPU リダクション化", B: 3, E: 3, G: 4 },
    { Id: "GBI-04", Title: "Square 専用 JIT カーネルのディスパッチ", B: 2, E: 4, G: 4 },
    { Id: "GBI-05", Title: "Lehmer's GCD の大桁並列化", B: 1, E: 1, G: 2 },
    { Id: "GBI-06", Title: "式木の Kernel Fusion", B: 1, E: 1, G: 4 },

    // --- GSH (GSharp トランスパイラ / ランタイム) 領域 ---
    { Id: "GSH-01", Title: "三項演算子 (`? :`) の複雑式展開", B: 5, E: 4, G: 5 },
    { Id: "GSH-02", Title: "タプル構文の一般化", B: 4, E: 3, G: 4 },
    { Id: "GSH-03", Title: "ローカル関数の非サポート", B: 5, E: 3, G: 4 },
    { Id: "GSH-04", Title: "スタック固定長配列・stackalloc の限定性", B: 5, E: 3, G: 4 },
    { Id: "GSH-05", Title: "ジェネリクス (<T>) の制約解決", B: 3, E: 1, G: 3 },
    { Id: "GSH-06", Title: "PTX / C++ コンパイルエラーの行番号マッピング", B: 2, E: 3, G: 5 },
    { Id: "GSH-07", Title: "JIT ディスクキャッシュ機構", B: 1, E: 2, G: 5 },

    // --- GEN (GSharp ソースジェネレータ / ランタイム基盤) 領域 ---
    { Id: "GEN-01", Title: "C# ラッパーと CUDA 連携の基本機能（JITバインディング）", B: 5, E: 5, G: 5 },
    { Id: "GEN-02", Title: "NVRTC コンパイルエラーの例外ラップ", B: 4, E: 5, G: 5 },

    // --- SYN (C# 構文・シンタックスシュガー) 領域 ---
    { Id: "SYN-01", Title: "Switch 式 (switch expression)", B: 4, E: 4, G: 4 },
    { Id: "SYN-02", Title: "パターンマッチング (is / 結合 / リレーショナル)", B: 4, E: 3, G: 5 },
    { Id: "SYN-03", Title: "インライン out var 変数宣言", B: 5, E: 3, G: 5 },
    { Id: "SYN-04", Title: "Null合体 / 複合代入 (??, ??=)", B: 4, E: 4, G: 5 },
    { Id: "SYN-05", Title: "三項演算子 (`? :`) の型変換・ネスト", B: 4, E: 4, G: 4 },
    { Id: "SYN-06", Title: "ref 条件式 (ref ternary)", B: 2, E: 3, G: 2 },
    { Id: "SYN-07", Title: "ローカル関数 (local functions)", B: 5, E: 3, G: 4 },
    { Id: "SYN-08", Title: "ラムダ式・無名関数", B: 5, E: 2, G: 5 },
    { Id: "SYN-09", Title: "デフォルトリテラル (default)", B: 4, E: 5, G: 5 },
    { Id: "SYN-11", Title: "Index & Range 構文 (^1, [1..5])", B: 3, E: 3, G: 4 },
    { Id: "SYN-12", Title: "コレクション式 ([1, 2, 3])", B: 3, E: 4, G: 4 },
    { Id: "SYN-13", Title: "stackalloc と固定長スタック配列", B: 5, E: 3, G: 4 },
    { Id: "SYN-14", Title: "fixed バッファ・ポインタピン留め", B: 4, E: 4, G: 3 },
    { Id: "SYN-15", Title: "Span<T> / ReadOnlySpan<T> のカーネル内使用", B: 5, E: 1, G: 5 },
    { Id: "SYN-16", Title: "2次元インデクサ / 多次元配列", B: 3, E: 3, G: 2 },
    { Id: "SYN-17", Title: "Target-typed new() のネスト完全対応", B: 4, E: 4, G: 5 },
    { Id: "SYN-18", Title: "構造化束縛・タプル宣言分解", B: 4, E: 3, G: 4 },
    { Id: "SYN-19", Title: "構造体のプライマリコンストラクタ", B: 3, E: 4, G: 3 },
    { Id: "SYN-20", Title: "record struct と with 式", B: 3, E: 3, G: 3 },
    { Id: "SYN-21", Title: "構造体内インデクサ (this[int index])", B: 3, E: 3, G: 3 },
    { Id: "SYN-22", Title: "構造体内演算子オーバーロード", B: 4, E: 3, G: 4 },
    { Id: "SYN-23", Title: "readonly struct / readonly メソッド", B: 2, E: 5, G: 3 },
    { Id: "SYN-24", Title: "enum の基底型指定とビットフラグ", B: 3, E: 4, G: 4 },
    { Id: "SYN-25", Title: "文字列補間 ($\"\") のデバッグ出力化", B: 3, E: 3, G: 5 },
    { Id: "SYN-26", Title: "UTF-8 文字列リテラル", B: 2, E: 4, G: 2 },
    { Id: "SYN-27", Title: "nameof(...) 式の静的文字列解決", B: 3, E: 4, G: 4 },
    { Id: "SYN-28", Title: "[MethodImpl(AggressiveInlining)] マッピング", B: 4, E: 5, G: 5 },
    { Id: "SYN-29", Title: "[LoopUnroll(N)] 属性によるアンロール制御", B: 3, E: 5, G: 4 },
    { Id: "SYN-30", Title: "ジェネリック構造体 (struct Vector<T>)", B: 4, E: 1, G: 4 },
    { Id: "SYN-31", Title: "アンマネージド関数ポインタ", B: 2, E: 2, G: 1 },
    { Id: "SYN-32", Title: "MemoryMarshal.Cast<TFrom, TTo>", B: 3, E: 4, G: 3 },
    { Id: "SYN-33", Title: "ref フィールド / ref struct メンバ", B: 3, E: 2, G: 3 },
    { Id: "SYN-34", Title: "ハードウェア SIMD ベクトル (Vector128)", B: 4, E: 1, G: 5 },
    { Id: "SYN-35", Title: "ビット演算 Intrinsics (BitOperations)", B: 5, E: 5, G: 5 },
    { Id: "SYN-36", Title: "ポインタ後置増減", B: 4, E: 5, G: 4 },
    { Id: "SYN-37", Title: "Unsafe クラスの低レベルポインタ演算", B: 3, E: 4, G: 4 },
    { Id: "SYN-38", Title: "固定長バッファ (fixed uint buffer[64])", B: 4, E: 3, G: 3 },
    { Id: "SYN-39", Title: "明示的 / 暗黙的型変換演算子", B: 3, E: 3, G: 4 },
    { Id: "SYN-40", Title: "自動実装プロパティ", B: 3, E: 4, G: 4 },
    { Id: "SYN-41", Title: "計算式プロパティ (=>)", B: 3, E: 4, G: 4 },
    { Id: "SYN-42", Title: "静的抽象メンバ (static abstract)", B: 3, E: 1, G: 2 },
    { Id: "SYN-43", Title: "checked / unchecked ブロック・式", B: 2, E: 4, G: 3 },
    { Id: "SYN-44", Title: "拡張メソッド (this 引数)", B: 4, E: 4, G: 5 },
    { Id: "SYN-45", Title: "Nullable 値型", B: 3, E: 3, G: 3 },
    { Id: "SYN-46", Title: "ワープシャッフル・同期プリミティブ", B: 4, E: 4, G: 5 },
    { Id: "SYN-47", Title: "動的共有メモリ", B: 4, E: 3, G: 5 },
    { Id: "SYN-48", Title: "グリッド協調グループ同期", B: 2, E: 1, G: 2 },
    { Id: "SYN-49", Title: "アトミック演算完全マッピング", B: 4, E: 4, G: 5 },
    { Id: "SYN-50", Title: "非同期コピー / TMA", B: 1, E: 1, G: 3 },
    { Id: "SYN-57", Title: "リストパターン", B: 2, E: 2, G: 2 },
    { Id: "SYN-58", Title: "プロパティパターン", B: 2, E: 3, G: 2 },
    { Id: "SYN-59", Title: "位置パターン", B: 2, E: 3, G: 2 },
    { Id: "SYN-60", Title: "ラムダ式のデフォルト引数", B: 2, E: 4, G: 3 },
    { Id: "SYN-61", Title: "ラムダ式の明示的戻り値型指定", B: 2, E: 4, G: 3 },
    { Id: "SYN-62", Title: "scoped ref / scoped Span<T>", B: 2, E: 4, G: 2 },
    { Id: "SYN-63", Title: "Unsafe.NullRef<T>() / IsNullRef", B: 3, E: 4, G: 3 },
    { Id: "SYN-64", Title: "半精度浮動小数点数 (Half)", B: 2, E: 3, G: 2 },
    { Id: "SYN-65", Title: "ネイティブ整数 (nint, nuint)", B: 3, E: 4, G: 3 },
    { Id: "SYN-66", Title: "128ビット整数", B: 2, E: 3, G: 2 },
    { Id: "SYN-67", Title: "BFloat16 / FP8 型", B: 1, E: 2, G: 1 },
    { Id: "SYN-68", Title: "Math / MathF / Math.Clamp 関数群", B: 4, E: 5, G: 5 },
    { Id: "SYN-69", Title: "64ビット飽薄 / キャリー演算", B: 3, E: 3, G: 3 },
    { Id: "SYN-70", Title: "foreach ループ on Span<T>", B: 4, E: 3, G: 5 },
    { Id: "SYN-71", Title: "switch 内の goto case", B: 2, E: 3, G: 1 },
    { Id: "SYN-72", Title: "多重ループ脱出 (名前付き break)", B: 2, E: 4, G: 2 },
    { Id: "SYN-73", Title: "Debug.Assert / Trace.Assert", B: 3, E: 4, G: 4 },
    { Id: "SYN-74", Title: "CallerMemberName / CallerLineNumber", B: 3, E: 3, G: 3 },
    { Id: "SYN-75", Title: "[Conditional(\"DEBUG\")] 属性", B: 3, E: 4, G: 4 },
    { Id: "SYN-76", Title: "コンパイル時定数評価 (constexpr 伝播)", B: 3, E: 4, G: 3 },
    { Id: "SYN-77", Title: "インライン PTX / C++ 埋め込み", B: 5, E: 5, G: 5 },
    { Id: "SYN-78", Title: "テンソルコア・WMMA", B: 2, E: 2, G: 2 },
    { Id: "SYN-79", Title: "ファイルローカル型 (file struct)", B: 1, E: 4, G: 1 },
    { Id: "SYN-80", Title: "params Span<T>", B: 2, E: 3, G: 3 },
    { Id: "SYN-81", Title: "ref readonly 戻り値", B: 2, E: 4, G: 2 },
    { Id: "SYN-82", Title: "volatile フィールド修飾子", B: 2, E: 3, G: 2 },
    { Id: "SYN-83", Title: "[module: SkipLocalsInit] 属性", B: 1, E: 5, G: 1 },
    { Id: "SYN-84", Title: "タプル宣言分解の foreach ループ", B: 2, E: 3, G: 2 },
    { Id: "SYN-86", Title: "関数ポインタの呼出規約", B: 1, E: 4, G: 1 },
    { Id: "SYN-87", Title: "#line hidden / #line default", B: 1, E: 3, G: 1 },
    { Id: "SYN-88", Title: "暗黙のスタック配列 (stackalloc[])", B: 2, E: 4, G: 2 },
    { Id: "SYN-90", Title: "複雑な制御フロー（if-else ネストとループの複合）", B: 5, E: 5, G: 5 },
    { Id: "SYN-91", Title: "基本制御構文（switch, do-while, 論理演算）", B: 5, E: 5, G: 5 },
    { Id: "SYN-92", Title: "ユーザー定義構造体のカーネル引数および操作", B: 5, E: 5, G: 5 },
    { Id: "SYN-93", Title: "デバイスメソッド ([GpuDevice]) の呼び出し・再利用", B: 5, E: 5, G: 5 }
];

// =============================================================================
// スコア算出 & 分類ロジック (Scoring & Classification)
// =============================================================================

/**
 * 各タスクの優先度スコアを計算し、Tier を割り当ててマップを返す
 * @param {Array} taskList 
 * @returns {{ tasks: Array, taskMap: Object, tiers: { tier1: Array, tier2: Array, tier3: Array, tier4: Array } }}
 */
function processTasks(taskList) {
    const taskMap = {};

    taskList.forEach(t => {
        t.Score = (t.B * WEIGHTS.BLOCKER) + (t.E * WEIGHTS.EASE) + (t.G * WEIGHTS.GENERALITY);

        if (t.Score >= TIER_THRESHOLDS.TIER_1) {
            t.Tier = 1;
        } else if (t.Score >= TIER_THRESHOLDS.TIER_2) {
            t.Tier = 2;
        } else if (t.Score >= TIER_THRESHOLDS.TIER_3) {
            t.Tier = 3;
        } else {
            t.Tier = 4;
        }

        t.FormattedTier = `**Tier ${t.Tier}** (${t.Score})`;
        taskMap[t.Id] = t;
    });

    const sortedTasks = [...taskList].sort((a, b) => b.Score - a.Score);

    const tiers = {
        tier1: sortedTasks.filter(t => t.Tier === 1),
        tier2: sortedTasks.filter(t => t.Tier === 2),
        tier3: sortedTasks.filter(t => t.Tier === 3),
        tier4: sortedTasks.filter(t => t.Tier === 4)
    };

    return { tasks: sortedTasks, taskMap, tiers };
}

// =============================================================================
// ドキュメント生成ロジック (Markdown & Mermaid Generation)
// =============================================================================

/**
 * 指定 Tier のドメイン別タスク数をカウント
 */
function countDomains(tierTasks) {
    return {
        SYN: tierTasks.filter(t => t.Id.startsWith("SYN")).length,
        GBI: tierTasks.filter(t => t.Id.startsWith("GBI")).length,
        GSH: tierTasks.filter(t => t.Id.startsWith("GSH")).length,
        GEN: tierTasks.filter(t => t.Id.startsWith("GEN")).length
    };
}

/**
 * Mermaid フローチャート用サブグラフ文字列を生成
 */
function generateSubgraph(name, label, tierTasks) {
    if (tierTasks.length === 0) return "";
    const counts = countDomains(tierTasks);
    let sub = `    subgraph ${name}["${label}"]\n        direction LR\n`;

    const nodes = [];
    if (counts.GEN > 0) nodes.push(`        ${name}_GEN["GEN (ジェネレータ基盤): ${counts.GEN}件"]`);
    if (counts.SYN > 0) nodes.push(`        ${name}_SYN["SYN (構文): ${counts.SYN}件"]`);
    if (counts.GSH > 0) nodes.push(`        ${name}_GSH["GSH (トランスパイラ): ${counts.GSH}件"]`);
    if (counts.GBI > 0) nodes.push(`        ${name}_GBI["GBI (GPU基盤): ${counts.GBI}件"]`);

    sub += nodes.join("\n") + "\n";
    sub += `    end\n`;
    return sub;
}

/**
 * 全体開発ロードマップ (Mermaid) とマイルストーン一覧の Markdown 文字列を生成
 */
function generateMermaidAndMilestones(tiers) {
    let mermaid = "```mermaid\nflowchart TD\n";
    mermaid += generateSubgraph("Phase1", "Phase 1: Tier 1 (最優先)", tiers.tier1) + "\n";
    mermaid += generateSubgraph("Phase2", "Phase 2: Tier 2 (準優先)", tiers.tier2) + "\n";
    mermaid += generateSubgraph("Phase3", "Phase 3: Tier 3 (並行タスク)", tiers.tier3) + "\n";
    mermaid += generateSubgraph("Phase4", "Phase 4: Tier 4 (後回し)", tiers.tier4) + "\n";

    if (tiers.tier1.length > 0 && tiers.tier2.length > 0) mermaid += "    Phase1 --> Phase2\n";
    if (tiers.tier2.length > 0 && tiers.tier3.length > 0) mermaid += "    Phase2 --> Phase3\n";
    if (tiers.tier3.length > 0 && tiers.tier4.length > 0) mermaid += "    Phase3 --> Phase4\n";
    mermaid += "```\n\n";

    let milestones = "### 直近のマイルストーン & 推奨着手順\n\n";

    function generateMilestoneList(milestoneIndex, label, tierTasks) {
        if (tierTasks.length === 0) return "";
        let list = `${milestoneIndex}. **マイルストーン ${milestoneIndex}: ${label}**\n`;
        for (const t of tierTasks) {
            list += `    - **${t.Id}**: ${t.Title} (Score: ${t.Score})\n`;
        }
        return list;
    }

    milestones += generateMilestoneList(1, "Tier 1 (即効性の高いブロッカー解放)", tiers.tier1);
    milestones += generateMilestoneList(2, "Tier 2 (難易度が高い重要インフラ)", tiers.tier2);
    milestones += generateMilestoneList(3, "Tier 3 (並行作業・隙間タスク)", tiers.tier3);
    milestones += generateMilestoneList(4, "Tier 4 (後回し・ニッチ領域)", tiers.tier4);

    return mermaid + milestones;
}

// =============================================================================
// ファイル同期処理 (File Updating & Synchronization)
// =============================================================================

/**
 * 各個別タスク管理 Markdown のテーブル行を更新
 */
function updateIndividualMarkdownTables(docsDir, taskMap) {
    const filesToUpdate = ['gbi_roadmap.md', 'gsharp_transpiler_issues.md', 'gsharp_syntax_support.md', 'gsharp_generator_core.md'];

    for (const file of filesToUpdate) {
        const filePath = path.join(docsDir, file);
        if (!fs.existsSync(filePath)) {
            console.error(`[Error] File not found: ${filePath}`);
            continue;
        }

        const lines = fs.readFileSync(filePath, 'utf8').split('\n');
        let inTable = false;
        let idColIndex = -1;
        let tierColIndex = -1;

        for (let i = 0; i < lines.length; i++) {
            const line = lines[i];

            // テーブルヘッダーの検出
            if (line.trim().startsWith('|') && line.includes('ID')) {
                inTable = true;
                const cols = line.split('|').map(c => c.trim());
                idColIndex = cols.findIndex(c => c === 'ID');
                tierColIndex = cols.findIndex(c => c === '優先度' || c.includes('優先度') || c === '難易度');

                if (tierColIndex !== -1) {
                    cols[tierColIndex] = '優先度 (Tier)';
                    lines[i] = `| ${cols.slice(1, -1).join(' | ')} |`;
                }
            } else if (inTable && line.trim().startsWith('|') && line.includes(':---')) {
                // セパレータ行はスキップ
            } else if (inTable && line.trim().startsWith('|')) {
                // データ行の更新
                const cols = line.split('|').map(c => c.trim());

                if (cols.length > idColIndex && cols.length > tierColIndex && idColIndex > 0 && tierColIndex > 0) {
                    const rawId = cols[idColIndex].replace(/\*/g, '').trim();

                    if (taskMap[rawId]) {
                        cols[tierColIndex] = taskMap[rawId].FormattedTier;
                        lines[i] = `| ${cols.slice(1, -1).join(' | ')} |`;
                    }
                }
            } else if (inTable && !line.trim().startsWith('|')) {
                inTable = false;
            }
        }

        fs.writeFileSync(filePath, lines.join('\n'), 'utf8');
        console.log(`[Synced] ${file}`);
    }
}

/**
 * docs/roadmap-index.md の全体ロードマップ領域を更新
 */
function updateRoadmapIndex(indexFilePath, generatedContent) {
    if (!fs.existsSync(indexFilePath)) {
        console.warn(`[Warn] File not found: ${indexFilePath}`);
        return;
    }

    let content = fs.readFileSync(indexFilePath, 'utf8');
    const regex = /<!-- BEGIN GENERATED ROADMAP -->[\s\S]*?<!-- END GENERATED ROADMAP -->/;

    if (regex.test(content)) {
        content = content.replace(regex, `<!-- BEGIN GENERATED ROADMAP -->\n\n${generatedContent}\n<!-- END GENERATED ROADMAP -->`);
        fs.writeFileSync(indexFilePath, content, 'utf8');
        console.log(`[Synced] roadmap-index.md`);
    } else {
        console.warn(`[Warn] Magic comments (<!-- BEGIN GENERATED ROADMAP -->) not found in ${indexFilePath}`);
    }
}

/**
 * コンソールへ優先度サマリーを出力
 */
function printSummary(tiers) {
    console.log("=== Henicos (G#) 開発タスク優先度スコア一覧 ===\n");

    console.log("### Tier 1 (Score >= 36) : 最優先 (Blocker=High, Ease=High)");
    tiers.tier1.forEach(t => {
        console.log(`- ${t.Id}: ${t.Title} [Score: ${t.Score} (B:${t.B}, E:${t.E}, G:${t.G})]`);
    });

    console.log("\n### Tier 2 (28 <= Score < 36) : 準優先 (Blocker=High, Ease=Low OR Blocker=Med, Ease=Med)");
    tiers.tier2.forEach(t => {
        console.log(`- ${t.Id}: ${t.Title} [Score: ${t.Score} (B:${t.B}, E:${t.E}, G:${t.G})]`);
    });

    console.log("\n### Tier 3 (20 <= Score < 28) : 並行・隙間タスク (Blocker=Low, Ease=High)");
    tiers.tier3.forEach(t => {
        console.log(`- ${t.Id}: ${t.Title} [Score: ${t.Score} (B:${t.B}, E:${t.E}, G:${t.G})]`);
    });

    console.log("\n### Tier 4 (Score < 20) : 後回し・ニッチ (Blocker=Low, Ease=Low)");
    tiers.tier4.forEach(t => {
        console.log(`- ${t.Id}: ${t.Title} [Score: ${t.Score} (B:${t.B}, E:${t.E}, G:${t.G})]`);
    });
}

// =============================================================================
// メインエントリポイント (Main CLI Entrypoint)
// =============================================================================

function main() {
    const { taskMap, tiers } = processTasks(TASKS);

    if (process.argv.includes('--update-docs')) {
        const rootDir = path.join(__dirname, '..');
        const docsDir = path.join(rootDir, 'docs', 'roadmaps');
        const indexFilePath = path.join(rootDir, 'docs', 'roadmap-index.md');

        console.log("=== ロードマップドキュメントの同期を開始 ===");
        updateIndividualMarkdownTables(docsDir, taskMap);

        const generatedContent = generateMermaidAndMilestones(tiers);
        updateRoadmapIndex(indexFilePath, generatedContent);

        console.log("=== 同期完了 ===");
    } else {
        printSummary(tiers);
    }
}

main();
