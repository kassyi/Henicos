---
description: 仕様書の一括検証・同期ワークフロー。C#実装を唯一の正（Single Source of Truth）とし、デグレ検知ガード、MSDN流の解説保全・マージ、実動サンプル検証を伴って仕様書（英語Markdown）を完全同期します。
---

# バッチ仕様書同期プロセス (Batch Spec-to-Code Sync)

このワークフローは、コンテキストウインドウの枯渇（LLMの迷子現象）を防ぐため、指定されたフォルダ以下のソースコード（C#）を数ファイルずつ「チャンク単位（3〜5ファイル）」で読み込み、C#実装を最新の正（Single Source of Truth）として仕様書（`docs/spec/en/`）へ同期するプロセスです。

本ワークフローは以下の**4原則**を徹底します：

1. **デグレ検知ガード（都度対話確認）**: 実装から消えている機能を見つけても**即座に仕様書を削らない**。リファクタリング漏れやマージミスによる機能欠落（デグレ）を疑い、チャンクごとにユーザーへ対話確認する。肯定的な回答（仕様変更/意図的削除）が得られて初めて仕様書から削る。
2. **追加・変更の自動同期と事後報告**: コードに新設されたAPIやシグネチャ変更は、自動で仕様書に反映し、進捗レポートで事後報告する。
3. **MSDN流の「骨格同期」と「解説テキストの保全・マージ」**: シグネチャや引数テーブルはコードから最新化するが、仕様書側のリッチな解説（アーキテクチャ背景、[!IMPORTANT]、ユースケース）をXMLコメントの短いテキストで上書き破壊しない。
4. **仕様書サンプルの実動検証と最小依存証明（Living Documentation）**: 仕様書内のすべてのコードブロックを対象にするのではなく、**`## Examples` セクションに記述された実例コードのみ**を動作保証の対象とします。エージェントは自ら推測でビルド環境を作るのではなく、プロジェクト内に用意されている専用のDocTestスクリプト（実行例: `just doctest tmp/doctest/Program.cs "Kassyi.GSharp.Core"`）にスニペットを流し込みます。この際、**「Henicosの全アセンブリを渡して無理やりパスさせる」という手抜き（怠慢）は固く禁じます**。必ず試行錯誤を通じて「必要最低限のアセンブリ」を特定し、その最小要件を仕様書に明記することで読者に親切なドキュメントを作成します。

---

## 0. セッションの初期化 (Initialization)

本タスクを開始した直後に、必ず以下の初期化プロセスを実行してください。
すでに `./tmp/update-spec/sync-context.json` が存在する場合はこの手順をスキップし、`STEP 1` に進んでください。

1. ユーザーから「`src/Stdlib/`以下全部」のような自然言語で対象範囲の指示があった場合、それを適切なディレクトリパス（例: `src/Stdlib/Kassyi.GSharp.Stdlib.Math/BigInt/`）に変換した上で、該当範囲にあるすべての対象ソースファイル（`.cs`）を抽出します（`fd -e cs . <変換したパス>` ツール等を使用）。指定がなければユーザーに確認してください。
2. ディレクトリ `./tmp/update-spec/` が存在しない場合は作成し、`sync-context.json` を以下のスキーマで新規作成して保存します。
    ```json
    {
        "target_scope": "src/...",
        "pending_targets": ["src/.../Yyy.cs", "src/.../Zzz.cs"],
        "completed_targets": [],
        "applied_changes": [],
        "suspected_regressions": []
    }
    ```

---

## STEP 1: バッチ検証・即時デグレ確認セッション（チャンク処理）

コンテキスト枯渇を防ぐため、1回の処理では **必ず3〜5ファイル程度** をピックアップして進めてください。

### 1. 実行手順

1. `./tmp/update-spec/sync-context.json` を読み込みます。
2. `pending_targets` から **3〜5ファイル** をピックアップします。
3. ピックアップした各ソースコードファイルを `view_file` ツールで**実際に読み込みます**。

#### A. C#実装コードの解析 (Code Analysis)

読み込んだC#ファイルから、外部に公開されるすべての公開API（`public` / `protected`）の構文構造を走査・抽出します：

- **型宣言**: クラス、構造体、インターフェイス、列挙型、ジェネリック制約
- **コンストラクタ**: すべてのオーバーロードと引数
- **プロパティ**: 型、アクセス権（get/set/init）
- **メソッド**: すべてのオーバーロード（同名メソッドの別シグネチャを絶対に見落とさないこと）、引数型、引数名、デフォルト値、戻り値型
- **演算子**: `operator +`, `explicit/implicit` など
- **XMLドキュメントコメント**: `<summary>`, `<param>`, `<typeparam>`, `<returns>`, `<exception>`, `<remarks>`

#### B. 関連仕様書（Markdown）の特定と照合 (Fact-Checking)

`rg` などを使い、`docs/spec/en/` 配下にある関連仕様書を特定・読み込み、C#実装と照合して以下の**3分類**に判定します：

1. **[Type A: 新規追加 (Addition)]**:
    - コード側にあるが、仕様書に存在しないAPI、新設オーバーロード、新プロパティなど。
    - **対応**: 仕様書へ自動追記（事後報告）。
2. **[Type B: 仕様更新 (Modification)]**:
    - 両方に存在するが、シグネチャ（引数型、戻り値、デフォルト値）や例外型、Remarksの制約条件に乖離があるもの。
    - **対応**: 骨格をコードに合わせて更新し、解説をマージ（事後報告）。
3. **[Type C: デグレ疑い (Missing / Removal)]**:
    - **仕様書に記載されているが、現在のコード（およびアセンブリ）内に存在しない機能・メソッド・オーバーロード**。
    - **対応**: **絶対に仕様書から削除しない！** `suspected_regressions` に隔離し、セッション終了時にユーザーに対話確認する。

#### C. MSDN流の仕様書アップデート（Type A & B の自動反映）

`replace_file_content` 等で該当の仕様書Markdownを更新します：

- **骨格の同期**: Constructors, Properties, Methods の一覧テーブルおよび各小見出し（シグネチャ、Parameters, Returns, Exceptions）をコードの最新状態と完全に一致させます。
- **リッチ解説の保全（重要）**:
    - 既存の仕様書に書かれている概念説明、アーキテクチャ背景、警告・注意点（`> [!IMPORTANT]` や `> [!NOTE]` など）は**最大限維持**します。
    - XMLドキュメントコメントの短い文章で既存のリッチ解説を上書き（縮小・破壊）してはいけません。
    - コード側で新たに `<exception>` や `<remarks>`（制約・実行環境等）が追加されていた場合のみ、既存解説に追記・マージします。
- **新規APIの解説**:
    - 新設されたメソッド等には、コードのXMLコメントを出発点として、末端開発者が必要とする文脈（BCL互換性、GPU制約など）を補足した英語テキストを書き下ろします。

#### D. 仕様書記載サンプルのスクリプト検証とアップデート (Living Documentation)

MSDNの規約に則り、シグネチャの定義ブロックと実動サンプルを明確に区別し、実例のみを専用スクリプトで検証します：

1. **対象スニペットの特定と抽出**: 仕様書（Markdown）内の **`## Examples` 見出し配下にあるC#コードブロック（```csharp）のみ**を抽出し、一時ファイル（例: `tmp/doctest/Program.cs`）に保存します。
2. **検証スクリプトへの流し込みと「最小依存」の証明 (Critical Step)**:
    - AI自身が推測でテストプロジェクトのガワやusingを追加・改造することは禁止します。
    - `just doctest <FilePath> "<AssemblyList>"` を実行して検証します。
        - 単一アセンブリの例: `just doctest tmp/doctest/Program.cs "Kassyi.GSharp.Core"`
        - 複数アセンブリの例: `just doctest tmp/doctest/Program.cs "Kassyi.GSharp.Core,Kassyi.GSharp.Runtime"` (カンマ区切りで指定)
    - **【厳格ルール】**: アセンブリリストに「全部入り（Core, Runtime, Stdlib等を全て指定）」を入れてパスさせてはいけません。必ず1つまたは最小限の推測からスタートし、`CS0246` 等のエラーが出たら必要なアセンブリを1つずつ足して、**本当に必要な最小限のアセンブリを特定**してください。
3. **エラー解析と能動的な修正**:
    - シグネチャ変更によるビルド落ちなど、純粋なスニペット側のエラーが出た場合は、最新の実装コードと照らし合わせてスニペットのロジック自体を能動的に修正します。
4. **仕様書への明記**:
    - 検証をパスした「生きたコード」を仕様書に書き戻すとともに、**特定した最小必須アセンブリのリストをコードブロックの直前に必ず明記**してください（例: `> [!NOTE] Requires assemblies: Kassyi.GSharp.Core`）。

#### E. 状態の保存

- 完了したファイルを `pending_targets` から `completed_targets` へ移動。
- 自動反映した変更を `applied_changes` に記録。
- 発見したデグレ疑いを `suspected_regressions` に記録。
- `sync-context.json` を上書き保存します。

---

### 2. セッションの終了（ハンドオフ & デグレ即時対話確認）

JSONファイルの保存が完了したら、途中の検証内容の長大なログは出力せず、以下のフォーマットに沿って**進捗レポート**を出力し、ユーザーの指示を待ってください。

#### 出力フォーマット

```markdown
### ⏳ 仕様書同期 進捗レポート (Chunk Processed)

#### ✅ 今回検証・反映したファイル

- `src/.../Yyy.cs` → `docs/spec/en/.../Yyy.md`
    - [Auto-Applied] 新設メソッド `Bar(int)` を仕様書に追加
    - [Auto-Applied] `Foo()` のシグネチャ変更を反映
- `src/.../Zzz.cs` → `docs/spec/en/.../Zzz.md`
    - 乖離なし（完全同期済み）

#### ⏳ 未検証のファイル (残り M 件)

- `src/...` (代表して数件を表示)
```

---

#### ⚠️ デグレ疑いがある場合のアラート（最優先でユーザーに確認）

`suspected_regressions`（実装から消えている機能）が1件以上検出された場合は、レポートの直後に必ず以下のアラートを出力してください：

```markdown
> [!WARNING]
>
> ### ⚠️ 【デグレ確認】実装コードから消失している機能を発見しました
>
> 以下の機能は仕様書に記載されていますが、現在のC#実装上に見当たりません。
>
> - **対象機能**: `TypeName.MethodName(Args...)`
> - **仕様書箇所**: `docs/spec/en/...md#Lxx-Lyy`
> - **確認事項**:
>     1. これは**意図した仕様変更（機能削除または名称変更）**ですか？
>     2. それとも、リファクタリング等のミスによる**実装側のデグレ（機能欠落バグ）**ですか？
>
> **回答のお願い:**
>
> - 「**仕様変更なので削除して**」→ 次のターンで仕様書から安全に削ります。
> - 「**デグレ（バグ）なので残して**」→ 仕様書は維持し、未解決の不具合としてバグレポートに記録します。
```

---

#### 次のアクションの選択肢

```markdown
---

### 次のアクションを選択してください

- **次のチャンクの検証を続ける場合**: 「**続きを検証して**」または「**y**」と入力してください（デグレ確認がある場合は併せてご回答ください）。
- **すべての検証が完了した場合**: 「**完了レポートを出力して**」と指示してください。
```

---

## STEP 2: 完了サマリー & デグレ報告 (Final Report)

すべての `pending_targets` が空になった場合、またはユーザーから終了を指示された場合、最終レポートを出力します：

1. **仕様書に自動同期された更新点サマリー**:
    - 追加された新設API一覧
    - 更新されたシグネチャ・引数一覧
2. **デグレ確認結果および未解決のバグ一覧**:
    - ユーザーから「デグレである」と判定され、仕様書を維持したままの実装バグ一覧（GitHub Issue起票用フォーマット）。
    - ユーザーの承認を得て仕様書から正式に削除した機能一覧。
3. `./tmp/update-spec/` の一時ファイルをクリーンアップ（またはアーカイブ）。

---

## 付録: API仕様書フォーマット標準 (MSDN / Microsoft Learn 規約)

本ワークフローにおいて、MSDNスタイルの厳格なフォーマットを適用するのは **`docs/spec/en/03_bcl_api/` 配下などのAPIリファレンスドキュメントのみ**です。

対象となるC#のソースコード（AST）を解析し、以下の厳格な構造規約を遵守して出力してください。**Markdownの見出しは最大でも `##`（H2）から開始すること**（`#`（H1）は使用禁止）。

### ゴールデンマスター・テンプレート

以下のテンプレート構造を**1文字のブレもなく**踏襲してください。該当しないセクション（例：プロパティがない等）は省略します。

````markdown
---
title: "[TypeName] [Class/Struct/Interface/Method] (G#)"
site: "G# Documentation"
published: "YYYY-MM-DDT00:00:00.000Z"
language: "en"
description: "Brief summary of the type."
---

## [TypeName] [Class/Struct/Interface]

## Definition

**Namespace:** `Namespace.Name`  
**Assembly:** `Assembly.Name.dll`

[Summary and architectural narrative. Preserve any existing rich explanations, [!IMPORTANT], [!NOTE], etc.]

```csharp
[Type Signature (e.g. public static class CudaEnvironment)]
```

## Constructors

| Name                            | Description    |
| ------------------------------- | -------------- |
| [TypeName(Args)](#typenameargs) | Brief summary. |

## TypeName(Args)

[Detailed description of the constructor]

```csharp
public TypeName(Args);
```

#### Parameters

`argName` _Type_<br>
Description of the parameter.

#### Exceptions

`ExceptionType`<br>
Condition for the exception.

## Properties

| Name                  | Description    |
| --------------------- | -------------- |
| [PropName](#propname) | Brief summary. |

## PropName

[Detailed description of the property]

```csharp
public Type PropName { get; }
```

## Methods

| Name                                | Description    |
| ----------------------------------- | -------------- |
| [MethodName(Args)](#methodnameargs) | Brief summary. |

## MethodName(Args)

[Detailed description of the method]

```csharp
public ReturnType MethodName(Args);
```

#### Parameters

`argName` _Type_<br>
Description of the parameter.

#### Returns

_ReturnType_<br>
Description of the return value.

#### Exceptions

`ExceptionType`<br>
Condition for the exception.

## Operators

| Name                     | Description                            |
| ------------------------ | -------------------------------------- |
| `operator +(Type, Type)` | Constructs a lazy addition expression. |

## Examples

### [Practical Use Case Title]

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.Math

```csharp
using System;

// Execution Context: Host (CPU) or G# Kernel (GPU)
// Code snippet using Top-Level Statements (No `class Program` / `static void Main()`)
```

## Remarks

[Detailed architectural constraints, performance notes, or threading behaviors.]

## See also

### BCL API > [Category]

- [Link Title](../00_index.md)
- (...other related classes/structs..)

### Sample Modules

- [Sample Modules Title](../00_index.md)
- (...other related projects..)
````

### ⚠️ フォーマットの絶対遵守ルール

1. **見出しレベル**: タイトル下のトップレベル見出しは `## [TypeName] [Kind]` にすること。`#` は使わない。
2. **メソッド・プロパティの詳細見出し**: 一覧表（Table）のリンク先となる見出しは `## MethodName(Args)` のように `##` で定義すること。
3. **引数・戻り値のフォーマット**:
    - `#### Parameters` や `#### Returns` は `####` を使用。
    - `argName _Type_<br>` のように、型はアンダースコア（または `<i>`）でイタリックにし、その後に `<br>` をつけて**改行**すること。説明文は次の行に書く。
4. **Examplesブロック**:
    - `class Program { static void Main() { ... } }` のようなボイラープレートは**絶対に書かない**こと。C# 9.0 のトップレベルステートメントを使用する。
    - スニペットの冒頭に `// Execution Context: Host (CPU)` などの実行コンテキストを明記すること。
    - `> [!NOTE] Requires assemblies: ...` を必ず挿入すること。
