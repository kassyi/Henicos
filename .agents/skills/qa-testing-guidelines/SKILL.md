---
name: qa-testing-guidelines
description: ガイドライン: C#およびGPU向けのテストコードを作成する際、ハッピーパスだけでなく、データ駆動境界値、並行ストレス、アライメント、異常系のテストを網羅し、Feature IDに基づくTrait属性や適切なファイル分離・命名規則を適用するための総合的QAルール。
---

# QA Testing Guidelines (エージェント向け総合テスト作成指針)

このプロジェクト（Henicos / GSharp）においてテストコード（xUnit）を新規作成または修正する際、エージェントは**必ず以下のQAテスト観点・構造ルール・Feature IDトレーサビリティ**を満たしているかを自己評価し、不足している場合は自律的にテストケースを拡張・追加・再構築しなければなりません。

## 0. 【必須】テスト先行AIバイブコーディング (Phase 1)

実装を開始する際は、**いかなるプロダクションコード（実装）を書くよりも先に、必ずテストコードを生成**してください（トランスパイラ関連タスクにおいては Golden Master / Snapshot Test を必須とし、それ以外は適切な Unit Test を記述します）。

- ロードマップタスクに着手する際の「共通の第1フェーズ」です。
- 正常系だけでなく、後述の「異常系テスト（Sad Path）」やQAの品質基準に基づき、**どのような例外がスローされるべきかまでを設計**してから実装をスタートさせます。

## 1. データ駆動境界値テスト (Data-Driven Edge Case Testing)

ハッピーパス（正常値）のテストだけでなく、必ず `[Theory]` を用いて極端な境界値を注入してください。

- **数値型**: `0`, `1`, `-1`, 最小値（`MinValue`）、最大値（`MaxValue`）、最上位ビットのみ1（`0x80000000`）、交互ビット（`0x55555555`）など。
- **制御構文**: `if` や `switch` の境界となる値（例: `> 10` なら `9`, `10`, `11`）、および `default` ルートへのフォールバック。
- **配列・Span**: 空の配列（長さ0）、1要素の配列、メモリ境界ギリギリのサイズ。

## 2. GPU固有の並行性・ストレステスト (Concurrency & Stress Testing)

スレッドやワープ（Warp）、共有メモリ、アトミック演算が絡む処理の場合、意図的に高負荷・高競合状態を作り出してテストしてください。

- **アトミック演算**: 同一のメモリアドレスに対して、数千スレッド（例: 256スレッド × 16ブロック = 4096スレッド）から同時に書き込み・加算（`AtomicAdd`等）を行い、結果が完全一致するか。
- **バリア同期**: `Gpu.SyncThreads()` の前後で、ワープ境界を越えた共有メモリの読み書きが正確に同期されているか。
- **ホスト側 (C#)**: `Logger` や JIT コンパイラ（`NvrtcCompiler`）など、スレッドセーフティが求められるコンポーネントに対して `Parallel.For` などを使い、多重アクセス時のデッドロックやクラッシュが起きないか検証する。この際、CUDA APIのThread Local Context制約に注意し、必要に応じてワーカースレッド内でコンテキストをバインドすること。

## 3. アライメント・メモリレイアウト検証 (Alignment & Padding Testing)

構造体（`struct`）やメモリブロックを扱う際、C#側とGPU（CUDA C++）側でメモリレイアウトが一致しているかを検証してください。

- `byte`, `double`, `short` などを混在させた構造体を定義し、意図しないパディングによるオフセットのズレ（アライメント違反）が起きないか。
- `MemoryMarshal.Cast` やポインタ演算において、境界をまたいだアクセスが安全に行われるか。

## 4. サッドパス・異常系テスト (Sad Path / Exception Testing)

エラーになるべき入力や状況を与えた際に、システムがクラッシュしたり無限ループに陥るのではなく、**適切にエラーを検知して安全に遮断すること**を検証してください。

- **静的解析 (Analyzer) / Source Generator**: サポートされていない構文や無効な型を使用した場合、コンパイルを通すのではなく、適切な `Diagnostic` (例: `GSH0001` 等) が発出されてビルドが中断されること。
- **実行時 (Runtime)**: 無効な引数（例: 存在しない関数名、壊れたPTX文字列、無効なアーキテクチャ指定 `sm_999`）を渡した際に、サイレントに無視するのではなく、明確な例外（`CudaException` など）がスローされること。

## 5. テスト命名規則とドメイン自然言語の徹底

コードベースが大規模化したため、**C# コード内への機能ID（Feature ID / チケット番号）のハードコードは廃止**されました。
テストコードは C# Framework Design Guidelines に準拠し、ドメインの意図を直接表現する自然言語で命名してください。なお、Jules やロードマップとのタスク連携はコード側ではなく外部メタデータ（JSON / `roadmap.json`）側で紐付けを管理します。

- **`[Trait("FeatureId", "...")]` の廃止**:
    - テストメソッドやクラスに `[Trait("FeatureId", "...")]` を付与しては**いけません**。
    - 分類が必要な場合は、カテゴリ Trait（例: `[Trait("Category", "HappyPath")]`, `[Trait("Category", "SadPath")]`, `[Trait("Category", "GpuExecution")]`）を使用してください。
    ```csharp
    [Theory]
    [Trait("Category", "SadPath")]
    public async Task GeneratorSnapshots_ThrowsOnInvalidSyntax(string fileName)
    ```
- **テストメソッド・クラス・ファイル命名規則**:
    - ファイル名、クラス名、メソッド名に `GEN-08_` や `GBI-06_` などの機能IDプレフィックスを含めず、テスト対象の振る舞いを表す自然言語名を使用してください（例: `KernelFusionTests.cs`、`GpuBigIntegerTests.cs`、`Test_MemoryPoolAllocation`、`Test_SwitchExpression_Transpilation`）。

## 6. GPUカーネルファイルの配置戦略 (Colocation vs Centralization)

テスト用カーネルや解析対象コード（`.cs` / `.cu.cs`）の配置は、用途に応じて以下の2つの戦略を厳密に使い分けてください。

- **実行系テスト（Integration / Runtime / Syntax）: 同一フォルダへの併置 (Colocation)**
    - 実際に `[GpuKernel]` をホストから Launch して実行結果を検証するテスト（例: `Syn01_91_ControlFlowTests.cs`）では、呼び出される対象のカーネルファイルを**テストファイルと同一のディレクトリ**に併置（例: `Syn01_91_ControlFlowKernels.cu.cs`）してください。
    - 理由: テストとカーネル間の高い凝集度が保たれ、開発者が実行コンテキストを容易に行き来できるようになるため。

- **静的解析・メタテスト（Analyzer / Generator）: TestCasesへの隔離 (Centralization)**
    - 解析対象の文字列（SyntaxTree）として読み込ませるコードや、意図的にコンパイルエラー・警告を発生させるコード（Sad Path）は、メインのテストアセンブリやビルドツリーを汚染しないよう、必ず **`TestCases/` ディレクトリ配下に隔離（Source Separation）** してください。
    - 配置先: `tests/Kassyi.GSharp.Tests/TestCases/Analyzers/` 等。