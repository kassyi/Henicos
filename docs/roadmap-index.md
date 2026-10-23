# G# (GSharp) & GpuBigInt (GBI) 開発ロードマップ & 進捗管理インデックス

---

## 1. ドキュメント構成 & 詳細進捗表一覧

ロードマップおよびタスクの進捗状況は、データ実体（SSOT）としての JSON ファイルと、具体的な C# 記述例・CUDA 変換仕様・背景を記述した Markdown 詳細進捗表の 2 層構造で管理されています。

### 1.1 マスターデータ & ツール

| リソース | 役割 | パス |
| :--- | :--- | :--- |
| **Data Source (SSOT)** | タスク一覧、優先度スコア、完了状態、実装メモの機械可読マスターデータ | [`data/roadmap_tasks.json`](data/roadmap_tasks.json) |
| **Interactive UI** | 人間が直感的にタスク状況を確認・更新するための Web アプリ (React+Vite) | [`tools/roadmap-ui/`](../tools/roadmap-ui/) |

### 1.2 領域別詳細進捗表 (仕様・変換方針・背景)

| ドキュメント | 担当領域 | 主な対象タスク | パス |
| :--- | :--- | :--- | :--- |
| **ジェネレータ・コア基盤** | Roslyn Source Generator、PTX/JIT エンジン、Vector 型マッピング、マーシャリング | `GEN-01` 〜 `GEN-19` | [`roadmaps/gsharp_generator_core.md`](roadmaps/gsharp_generator_core.md) |
| **C# 構文・シンタックスシュガー** | 制御構文、型推論、パターンマッチ、演算子、永久非対応構文 (`UNS-01`〜`14`) | `SYN-01` 〜 `SYN-96` | [`roadmaps/gsharp_syntax_support.md`](roadmaps/gsharp_syntax_support.md) |
| **トランスパイラ & ランタイム課題** | AST 走査、型解決、JIT キャッシュ、プロセス制御、デバッグ行マッピング | `GSH-01` 〜 `GSH-09` | [`roadmaps/gsharp_transpiler_issues.md`](roadmaps/gsharp_transpiler_issues.md) |
| **GpuBigInt (GBI) 基盤** | 多倍長整数 GPU 演算、NTT 乗算、Lehmer's GCD、メモリプール、演算子 | `GBI-01` 〜 `GBI-28` | [`roadmaps/gbi_roadmap.md`](roadmaps/gbi_roadmap.md) |
| **GpuBigFloat (GBF) 基盤** | 任意精度GPU浮動小数点演算、正規化、IEEE 754丸め | `GBF-01` 〜 | [`roadmaps/gbf_roadmap.md`](roadmaps/gbf_roadmap.md) |
| **GPUメモリ管理・所有権 (MEM)** | GPUリソースのゼロリーク静的解析、IDisposable/using、所有権移動、コレクション | `MEM-01` 〜 `MEM-07` | [`spec/en/04_guides/02_memory_safety_patterns.md`](spec/en/04_guides/02_memory_safety_patterns.md) |

---

## 2. 優先度スコアリング基準 (Priority Scoring Criteria)

タスクの優先度 (Tier) は、JSON に保存されている各指標に基づき、動的（UI 上など）に算出されます。

**指標定義 (1〜5点)**

- **$B$ (Blocker / 依存度)**: 他の実装やカーネルがブロックされる度合い（5: 完全ブロック / 1: 自己完結）
- **$D$ (Difficulty / 難易度)**: 実装難易度。高いほど難関・大設計（5: 大規模設計・リスク大 / 1: 極めて簡単・半日）
- **$G$ (Generality / 汎用性)**: コード内での出現頻度や利用される範囲（5: 日常的に全域で使用 / 1: 極めてニッチ）

**スコア計算式**
`Priority Score = (B * 4.0) + ((6 - D) * 3.0) + (G * 2.0)`

- **Tier 1 (最優先 / Score >= 36)**: ブロック度が高く、実装が容易（即効で他を解放）
- **Tier 2 (準優先 / Score >= 28)**: ブロック度は高いが、実装難易度も高い（早期に着手してパイプラインを疎通）
- **Tier 3 (並行タスク / Score >= 20)**: ブロック度は低いが、実装が容易（隙間時間に処理）
- **Tier 4 (後回し / Score < 20)**: ブロック度が低く、実装難易度も高い（性能チューニング、極小ニッチ構文など）
