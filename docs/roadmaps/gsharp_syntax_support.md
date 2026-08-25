# G# (GSharp) C# 構文・シンタックスシュガー対応状況 & 永久非対応構文一覧

最終更新日: 2026-08-25  
対象モジュール: `Kassyi.Generators.GSharp`

C# 開発者が無意識に書いてしまいがちな構文シュガー・現代 C# 機能を網羅し、G# のトランスパイラ（`RoslynToCudaVisitor`）における現状の挙動・未対応箇所・C++ へのマッピング方針を整理しました。

---

## 1. 制御構文・式レベル (Control Flow, Expressions & Lambdas)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-01** | **Switch 式 (`switch` expression)** | - [ ] | - [ ] | `var x = mode switch { 0 => a, 1 => b, _ => c };` | 文（statement）のみ対応。式構文はパース不能。 | 即時実行ラムダ（IIFE: `[&]() { switch(mode) { ... } }()`）または三項演算子の連鎖に展開。 | **Tier 1** (36) |
| **SYN-02** | **パターンマッチング (`is` / 結合 / リレーショナル)** | - [ ] | - [ ] | `if (val is >= 0 and < 1024)`<br>`if (state is State.Idle or State.Done)` | `BinaryExpression` 以外はパースエラー。 | 単純な論理式 `(val >= 0 && val < 1024)` へ展開。型パターンは C++ `auto` 束縛へ。 | **Tier 2** (35) |
| **SYN-03** | **インライン `out var` 変数宣言** | - [ ] | - [ ] | `Gpu.AddWithCarry(a, b, out var carry);` | `DeclarationExpressionSyntax` 未対応。 | C++ スコープ先頭に事前宣言を変数リフト（Variable Lifting）して渡す。 | **Tier 1** (39) |
| **SYN-04** | **Null合体 / 複合代入 (`??`, `??=`)** | - [ ] | - [ ] | `ptr = ptr ?? defaultPtr;`<br>`count ??= 0;` | 演算子未解決エラー。 | ポインタ/値の三項演算子 `(ptr != nullptr ? ptr : defaultPtr)` へ展開。 | **Tier 1** (38) |
| **SYN-05** | **三項演算子 (`? :`) の型変換・ネスト** | - [ ] | - [ ] | `var x = cond1 ? (cond2 ? 10u : 20u) : 30u;` | ネストや型昇格時に C++ 型不一致エラー。 | 括弧の完全保証と型キャストの明示的付与。 | **Tier 1** (36) |
| **SYN-06** | **`ref` 条件式 (`ref` ternary)** | - [ ] | - [ ] | `ref var x = ref (cond ? ref a : ref b);` | `RefExpressionSyntax` 未対応。 | C++ 参照束縛 `auto& x = (cond ? a : b);` へ展開。 | **Tier 3** (21) |
| **SYN-07** | **ローカル関数 (`local functions`)** | - [ ] | - [ ] | `void Add(int v) { sum += v; }` | 構文未対応（AST でスキップまたは構文エラー）。 | C++ キャプチャ付きラムダ（`auto Add = [&](int v) { sum += v; };`）へ展開。 | **Tier 1** (37) |
| **SYN-08** | **ラムダ式・無名関数 (Lambdas / Anonymous Functions)** | - [ ] | - [ ] | `Transform(buf, x => x * 2);` | デリゲート・ラムダ構文未対応。 | C++ ジェネリックラムダ (`[&](auto x) { return x * 2; }`) または関数オブジェクトへ展開。 | **Tier 1** (36) |
| **SYN-09** | **デフォルトリテラル (`default`, `default(T)`)** | - [ ] | - [ ] | `uint val = default;`<br>`MyStruct s = default;` | `LiteralExpression(DefaultLiteral)` の型推論欠落。 | C++ ゼロ初期化 `{}` または `MyStruct{}` へマッピング。 | **Tier 1** (41) |

---

## 2. コレクション・配列・メモリ操作 (Collections, Memory & Spans)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-11** | **Index & Range 構文 (`^1`, `[1..5]`)** | - [ ] | - [ ] | `var last = buffer[^1];`<br>`Span<uint> slice = buffer[1..4];` | `IndexExpressionSyntax` / `RangeExpressionSyntax` 未対応。 | `buffer[length - 1]` へのオフセット計算やポインタオフセット（`buffer + 1`）へ展開。 | **Tier 2** (29) |
| **SYN-12** | **コレクション式 (`[1, 2, 3]`) / スプレッド (`..`)** | - [ ] | - [ ] | `uint[] primes = [2, 3, 5, 7];` | C# 12 `CollectionExpressionSyntax` 未対応。 | C++ 初期化リスト `{ 2, 3, 5, 7 }` またはスタック配列初期化へ変換。 | **Tier 2** (32) |
| **SYN-13** | **`stackalloc` と固定長スタック配列** | - [ ] | - [ ] | `Span<uint> buf = stackalloc uint[32];` | スタック確保の C++ 変換が未整備。 | C++ ローカル配列 `uint32_t buf[32];` への宣言置換。 | **Tier 1** (37) |
| **SYN-14** | **`fixed` バッファ・ポインタピン留め** | - [x] | - [x] | `fixed (uint* p = &array[0]) { ... }` | 一部構文で型解決が不十分。 | C++ ネイティブポインタ宣言 `uint32_t* p = &array[0];` へ展開。 | **Tier 2** (34) |
| **SYN-15** | **`Span<T>` / `ReadOnlySpan<T>` のカーネル内使用** | - [ ] | - [ ] | `void Process(ReadOnlySpan<uint> data)` | 構造体定義・参照渡しがネイティブ未マッピング。 | ポインタと長さのペア、または C++20 `std::span` 相当の軽量構造体へマッピング。 | **Tier 2** (33) |
| **SYN-16** | **2次元インデクサ / 多次元配列** | - [ ] | - [ ] | `var v = grid[y, x];` | 多次元インデクサ未対応。 | フラット化された 1D インデックス計算 `grid[y * width + x]` へ展開。 | **Tier 3** (25) |

---

## 3. 構造体・型定義・オブジェクト構文 (Structs, Types & Object Model)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-17** | **Target-typed `new()` のネスト完全対応** | - [ ] | - [ ] | `ctx = new() { Operands = new() { ... } };` | ネストしたプロパティ初期化子で型推論が欠落。 | C++11 Uniform Initialization `{ ... }` への完全フォールバック。 | **Tier 1** (38) |
| **SYN-18** | **構造化束縛・タプル宣言分解** | - [ ] | - [ ] | `var (quotient, remainder) = DivMod(a, b);` | `TupleElementSyntax` の変数宣言未対応。 | C++17 Structured Binding (`auto [quotient, remainder] = ...`) または一時変数展開。 | **Tier 2** (33) |
| **SYN-19** | **構造体のプライマリコンストラクタ** | - [ ] | - [ ] | `struct Point(int X, int Y);` | C# 12 プライマリコンストラクタ構文未対応。 | C++ コンストラクタ `Point(int X, int Y) : X(X), Y(Y) {}` の自動出力。 | **Tier 2** (30) |
| **SYN-20** | **`record struct` と `with` 式** | - [ ] | - [ ] | `var next = current with { Index = i + 1 };` | `WithExpressionSyntax` 未対応。 | コピー生成＋メンバー上書き `auto next = current; next.Index = i + 1;` へ展開。 | **Tier 3** (27) |
| **SYN-21** | **構造体内インデクサ (`this[int index]`)** | - [ ] | - [ ] | `public uint this[int i] => Buffer[i];` | `IndexerDeclarationSyntax` 未対応。 | C++ `operator[](int i)` への変換。 | **Tier 3** (27) |
| **SYN-22** | **構造体内演算子オーバーロード** | - [ ] | - [ ] | `public static Point operator +(Point a, Point b)` | ユーザー定義演算子が C++ 演算子へ未マッピング。 | C++ `Point operator+(const Point& a, const Point& b)` へ展開。 | **Tier 2** (33) |
| **SYN-23** | **`readonly struct` / `readonly` メソッド** | - [ ] | - [ ] | `readonly struct Vector3 { public readonly float Length() => ...; }` | 修飾子が C++ `const` へ完全反映されていない。 | C++ `struct` メンバ関数の `const` 修飾子付与。 | **Tier 2** (29) |
| **SYN-24** | **`enum` の基底型指定とビットフラグ (`[Flags]`)** | - [ ] | - [ ] | `enum State : byte { None = 0, Active = 1 << 0 }` | 基底型やビット演算時の明示キャスト問題。 | C++ `enum class State : uint8_t` またはスコープ付き enum への展開。 | **Tier 2** (32) |

---

## 4. 文字列・デバッグ・メタプログラミング (Strings, Debugging & Attributes)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-25** | **文字列補間 (`$""`) のデバッグ出力化** | - [ ] | - [ ] | `Gpu.Print($"Thread {ThreadIndex.X}: val={val}");` | 文字列は GPU 禁止のためコンパイル不可。 | CUDA `printf("Thread %d: val=%u\n", threadIdx.x, val);` へフォーマット自動分解。 | **Tier 2** (31) |
| **SYN-26** | **UTF-8 文字列リテラル (`"..."u8`)** | - [ ] | - [ ] | `ReadOnlySpan<byte> tag = "KERNEL_V1"u8;` | `Utf8StringLiteralExpressionSyntax` 未対応。 | C++ `const char*` / `const uint8_t[]` リテラルへマッピング。 | **Tier 3** (24) |
| **SYN-27** | **`nameof(...)` 式の静的文字列解決** | - [ ] | - [ ] | `Gpu.Log(nameof(Device_Multiply));` | 構文木解決が未実装。 | コンパイル時に Roslyn 側で識別子文字列へ定数畳み込み置換。 | **Tier 2** (32) |
| **SYN-28** | **`[MethodImpl(AggressiveInlining)]` マッピング** | - [ ] | - [ ] | `[MethodImpl(MethodImplOptions.AggressiveInlining)]` | 属性が無視される。 | CUDA `__forceinline__` 修飾子の自動付与。 | **Tier 1** (41) |
| **SYN-29** | **`[LoopUnroll(N)]` 属性によるアンロール制御** | - [ ] | - [ ] | `[LoopUnroll(4)] for (int i = 0; i < 16; i++)` | 一律 `#pragma unroll` が付与される。 | 指定された数値に応じた `#pragma unroll N` / `#pragma unroll 1`（無効化）の出力。 | **Tier 2** (35) |
| **SYN-30** | **ジェネリック構造体 (`struct Vector<T>`)** | - [ ] | - [ ] | `struct Vector2<T> where T : unmanaged { public T X, Y; }` | C++ template への型パラメータバインド未対応。 | C++ `template<typename T> struct Vector2 { ... };` への自動展開。 | **Tier 3** (27) |

---

## 5. 高度なメモリ・ポインタ・アンセーフ機能 (Pointers, Memory & Unsafe)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-31** | **アンマネージド関数ポインタ (`delegate*`)** | - [ ] | - [ ] | `delegate* unmanaged[Cdecl]<uint, uint, uint> fnPtr;` | `FunctionPointerTypeSyntax` 未対応。 | C++ ネイティブ関数ポインタ `uint32_t (*fnPtr)(uint32_t, uint32_t)` へ展開。 | **Tier 4** (16) |
| **SYN-32** | **`MemoryMarshal.Cast<TFrom, TTo>`** | - [ ] | - [ ] | `var u64 = MemoryMarshal.Cast<uint, ulong>(u32Span);` | BCL 呼び出しとして未解決エラー。 | C++ `reinterpret_cast<uint64_t*>` へのインラインキャスト展開。 | **Tier 2** (30) |
| **SYN-33** | **`ref` フィールド / `ref struct` メンバ** | - [ ] | - [ ] | `ref struct Holder { public ref int Value; }` | `RefTypeSyntax` のフィールド宣言未対応。 | C++ 参照メンバ `int& Value;` またはポインタメンバへマッピング。 | **Tier 3** (24) |
| **SYN-34** | **ハードウェア SIMD ベクトル (`Vector128<T>`)** | - [ ] | - [ ] | `Vector128<uint> v = Vector128.Create(1u, 2u, 3u, 4u);` | `System.Runtime.Intrinsics` 未マッピング。 | CUDA ベクトル組込型 (`uint4`, `ulonglong2`, `int4`) への置換。 | **Tier 2** (29) |
| **SYN-35** | **ビット演算 Intrinsics (`BitOperations`)** | - [x] | - [x] | `BitOperations.LeadingZeroCount(w);`<br>`BitOperations.RotateLeft(w, 5);` | 実装完了。`Prelude.cu` に安全なヘルパー関数（`__gsharp_rotl64`, `__gsharp_tzc_32` 等）を追加し、C#仕様（0入力時の挙動）と完全互換を確保。 | CUDA 組込関数 (`__clz`, `__popc`, `__funnelshift_l`, `__funnelshift_r`) への完全網羅置換。 | **Tier 1** (45) |
| **SYN-36** | **ポインタ後置増減 (`ptr++`, `*(ptr++)`)** | - [x] | - [x] | `*destPtr++ = *srcPtr++;` | ポインタ式の順序・副作用の保証。 | C++ ネイティブポインタ後置インクリメント演算として安全に出力。 | **Tier 1** (39) |
| **SYN-37** | **`Unsafe` クラスの低レベルポインタ演算** | - [ ] | - [ ] | `Unsafe.Add(ref basePtr, offset);`<br>`Unsafe.ByteOffset(ref a, ref b);` | `System.Runtime.CompilerServices.Unsafe` 未解決。 | ネイティブ C++ ポインタ加算・アドレス差分計算へインライン展開。 | **Tier 2** (32) |
| **SYN-38** | **固定長バッファ (`fixed uint buffer[64]`)** | - [ ] | - [ ] | `public fixed uint Buffer[64];` | `FixedSizeBufferDeclarationSyntax` 未対応。 | C++ 構造体内埋め込み配列 `uint32_t Buffer[64];` へ展開。 | **Tier 2** (31) |

---

## 6. C# 高度な演算子・プロパティ・インターフェース (Operators, Properties & Interfaces)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-39** | **明示的 / 暗黙的型変換演算子 (`implicit/explicit`)** | - [ ] | - [ ] | `public static implicit operator uint(Word w) => w.Val;` | `ConversionOperatorDeclarationSyntax` 未対応。 | C++ ユーザー定義型変換演算子 `operator uint32_t() const` への展開。 | **Tier 2** (29) |
| **SYN-40** | **自動実装プロパティ (`{ get; set; }`)** | - [ ] | - [ ] | `public int Length { get; set; }` | 構造体内の自動プロパティが C++ 未展開。 | C++ パブリックメンバ変数、またはインライン getter/setter へ展開。 | **Tier 2** (32) |
| **SYN-41** | **計算式プロパティ (`=>`)** | - [ ] | - [ ] | `public int ByteCount => WordCount * 4;` | `PropertyDeclarationSyntax` の式ボディ未対応。 | C++ `const` メンバ関数 `int ByteCount() const { return WordCount * 4; }` へ展開。 | **Tier 2** (32) |
| **SYN-42** | **静的抽象メンバ (`static abstract` in interfaces)** | - [ ] | - [ ] | `interface IMonoid<T> { static abstract T Zero { get; } }` | C# 11 抽象静的インターフェース未対応。 | C++ テンプレート制約 (Concepts / CRTP パターン) への展開。 | **Tier 4** (19) |
| **SYN-43** | **`checked` / `unchecked` ブロック・式** | - [ ] | - [ ] | `unchecked { uint overflow = a + b; }` | 構文スキップまたは無視。 | キャリー/オーバーフロー検査（PTX `add.cc`）またはデフォルト無視の明示的処理。 | **Tier 3** (26) |
| **SYN-44** | **拡張メソッド (`this` 引数)** | - [ ] | - [ ] | `public static void Clear(this Span<uint> span)` | 拡張メソッド構文の解決。 | 通常の C++ 静的関数呼び出し `Clear(span)` へ展開。 | **Tier 1** (38) |
| **SYN-45** | **Nullable 値型 (`uint?`, `Nullable<T>`)** | - [ ] | - [ ] | `uint? maybeVal = null; if (maybeVal.HasValue) ...` | `NullableTypeSyntax` 未解決。 | C++ 軽量構造体 `struct { bool has_value; uint32_t value; }` への展開。 | **Tier 3** (27) |

---

## 7. 並列・同期・GPU 特化イディオム (Warp, Shared Memory & Concurrency)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-46** | **ワープシャッフル・同期プリミティブ** | - [ ] | - [ ] | `Warp.ShuffleDown(val, 1);`<br>`Warp.Ballot(condition);` | API 体系の未完全。 | CUDA `__shfl_down_sync`, `__ballot_sync`, `__all_sync`, `__any_sync` への直接置換。 | **Tier 1** (38) |
| **SYN-47** | **動的共有メモリ (`DynamicSharedMemory<T>`)** | - [ ] | - [ ] | `var shared = DynamicSharedMemory<uint>.Get();` | 動的共有メモリの型安全バインド未整備。 | `extern __shared__ uint32_t shared[];` への自動宣言・参照バインド。 | **Tier 2** (35) |
| **SYN-48** | **グリッド協調グループ同期 (`CooperativeGroups`)** | - [ ] | - [ ] | `Gpu.GridSync();` | 協調カーネルディスパッチとの連携不足。 | `cg::this_grid().sync()` への展開＋起動時 `cudaLaunchCooperativeKernel` 自動選択。 | **Tier 4** (15) |
| **SYN-49** | **アトミック演算完全マッピング (`Interlocked`)** | - [ ] | - [ ] | `Interlocked.Add(ref counter, 1);`<br>`Interlocked.CompareExchange(ref dest, val, comp);` | 一部アトミック演算のオーバーロード不足。 | CUDA `atomicAdd`, `atomicCAS`, `atomicExch`, `atomicMin`, `atomicMax` への完全マップ。 | **Tier 1** (38) |
| **SYN-50** | **非同期コピー / TMA (Tensor Memory Accelerator)** | - [ ] | - [ ] | `Gpu.AsyncCopy(destShared, srcGlobal, size);` | Hopper/Blackwell 世代の非同期パイプライン未対応。 | `cuda::memcpy_async` / PTX `cp.async` へのマッピング。 | **Tier 4** (13) |

---

## 8. 関数型・式・モダン制御構文 (Functional, Expressions & Advanced Patterns)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-57** | **リストパターン (`[1, .., var last]`)** | - [ ] | - [ ] | `if (slice is [0, .., 0])` | C# 11 `ListPatternSyntax` 未対応。 | 長さチェックと要素インデックス比較 `(len >= 2 && slice[0] == 0 && slice[len-1] == 0)` へ展開。 | **Tier 4** (18) |
| **SYN-58** | **プロパティパターン (`{ Length: > 0 }`)** | - [ ] | - [ ] | `if (ctx is { NumberOfWords: > 0 })` | `PropertyPatternClauseSyntax` 未対応。 | メンバアクセス比較 `(ctx.NumberOfWords > 0)` へ展開。 | **Tier 3** (21) |
| **SYN-59** | **位置パターン (`(0, 0)` / `(var x, > 10)`)** | - [ ] | - [ ] | `if (pos is (0, 0))` | `PositionalPatternClauseSyntax` 未対応。 | `(pos.Item1 == 0 && pos.Item2 == 0)` へ展開。 | **Tier 3** (21) |
| **SYN-60** | **ラムダ式のデフォルト引数 (C# 12)** | - [ ] | - [ ] | `var f = (int a, int b = 10) => a + b;` | ラムダ引数デフォルト値未対応。 | C++ ラムダ引数デフォルト値 `[](int a, int b = 10) { return a + b; }` へマッピング。 | **Tier 3** (26) |
| **SYN-61** | **ラムダ式の明示的戻り値型指定 (C# 10)** | - [ ] | - [ ] | `var f = uint () => 10u;` | `ParenthesizedLambdaExpressionSyntax` 戻り値未解決。 | C++ ラムダ後置戻り値 `[]() -> uint32_t { return 10u; }` へ展開。 | **Tier 3** (26) |
| **SYN-62** | **`scoped ref` / `scoped Span<T>` (C# 11)** | - [ ] | - [ ] | `void Process(scoped ref Context ctx)` | `scoped` キーワード未対応。 | C++ 参照修飾（スコープ内参照束縛）の保証。 | **Tier 3** (24) |
| **SYN-63** | **`Unsafe.NullRef<T>()` / `IsNullRef`** | - [ ] | - [ ] | `ref uint r = ref Unsafe.NullRef<uint>();` | ヌル参照イディオム未解決。 | C++ `nullptr` / ポインタ比較へインライン展開。 | **Tier 2** (30) |

---

## 9. 算術・数値型・ハードウェア数学 (Numerics, Math & HW Types)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-64** | **半精度浮動小数点数 (`Half` / `half2`)** | - [ ] | - [ ] | `Half h = (Half)1.5f;` | `System.Half` 未マッピング。 | CUDA `__half`, `__half2` (FP16 テンソル/SIMD) へ置換。 | **Tier 3** (21) |
| **SYN-65** | **ネイティブ整数 (`nint`, `nuint`)** | - [ ] | - [ ] | `nuint wordOffset = (nuint)i * 4;` | プラットフォーム依存整数未対応。 | CUDA `size_t` / `uintptr_t` へマッピング。 | **Tier 2** (30) |
| **SYN-66** | **128ビット整数 (`UInt128`, `Int128`)** | - [ ] | - [ ] | `UInt128 wide = (UInt128)a * b;` | .NET 7 `UInt128` 未マッピング。 | CUDA `unsigned __int128` (NVCC/GCC 拡張) へ展開。 | **Tier 3** (21) |
| **SYN-67** | **BFloat16 / FP8 型 (`BFloat16`, `fp8`)** | - [ ] | - [ ] | `BFloat16 bf = (BFloat16)1.0f;` | .NET 8 BFloat16 未マッピング。 | CUDA `__nv_bfloat16`, `__nv_fp8_e4m3` へ置換。 | **Tier 4** (12) |
| **SYN-68** | **`Math` / `MathF` / `Math.Clamp` 関数群** | - [ ] | - [ ] | `var clamped = Math.Clamp(v, min, max);`<br>`var fma = MathF.FusedMultiplyAdd(a, b, c);` | BCL 呼び出し未解決。 | CUDA 組込関数 (`__fmaf_rn`, `fminf`, `fmaxf`, `__sinf`, `__expf`) へ完全置換。 | **Tier 1** (41) |
| **SYN-69** | **64ビット飽和 / キャリー演算** | - [ ] | - [ ] | `Gpu.AddWithCarry64(a, b, out var carry);` | 64ビット版キャリー Intrinsics 不足。 | PTX `add.cc.u64`, `addc.cc.u64` インラインアセンブリへマッピング。 | **Tier 3** (27) |

---

## 10. ループ・ジャンプ・フロー制御 (Loops, Flow & Iterations)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-70** | **`foreach` ループ on `Span<T>` / 配列** | - [ ] | - [ ] | `foreach (var item in buffer)` | `ForEachStatementSyntax` 未対応。 | C++ 範囲ベース `for (const auto& item : buffer)` またはインデックス `for` へ展開。 | **Tier 2** (35) |
| **SYN-71** | **`switch` 内の `goto case` / `goto default`** | - [ ] | - [ ] | `goto case 0;` | `GotoCaseStatementSyntax` 未対応。 | C++ ラベルジャンプまたは fallthrough 展開。 | **Tier 4** (19) |
| **SYN-72** | **多重ループ脱出 (名前付き break / goto)** | - [ ] | - [ ] | `goto loop_exit;` | `GotoStatementSyntax` 制限。 | C++ `goto` ラベルジャンプへ安全に出力。 | **Tier 3** (24) |

---

## 11. デバッグ・契約・PTX/ハードウェア連携 (Contracts, PTX & Tensor Cores)

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-73** | **`Debug.Assert` / `Trace.Assert`** | - [ ] | - [ ] | `Debug.Assert(threadIdx < 32);` | デバッグアサート未解決。 | デバッグビルド時のみ `assert(...)` を出力し、リリース時はゼロコスト除去。 | **Tier 2** (32) |
| **SYN-74** | **`CallerMemberName` / `CallerLineNumber`** | - [ ] | - [ ] | `Gpu.Log("Check", [CallerLineNumber] int line = 0);` | 呼び出し元情報属性の未解決。 | C++ マクロ `__FILE__`, `__LINE__`, `__func__` への自動展開。 | **Tier 3** (27) |
| **SYN-75** | **`[Conditional("DEBUG")]` 属性** | - [ ] | - [ ] | `[Conditional("DEBUG")] void Dump() { ... }` | 条件付きコンパイル属性未展開。 | 指定シンボルが非定義時にメソッド呼び出しを AST レベルで完全除去。 | **Tier 2** (32) |
| **SYN-76** | **コンパイル時定数評価 (`constexpr` 伝播)** | - [ ] | - [ ] | `const int BlockSize = 256;` | C# `const` の C++ `constexpr` 化。 | C++ `constexpr int BlockSize = 256;` として出力し、静的配列長やアンロール回数へ伝播。 | **Tier 2** (30) |
| **SYN-77** | **インライン PTX / C++ 埋め込み (`Gpu.InlinePtx`)** | - [x] | - [x] | `Gpu.InlinePtx("extern __shared__ uint32_t s[];");`<br>`Gpu.InlinePtx($"asm volatile(\"...\");");` | 文字列リテラルおよび補間文字列による生 C++ / PTX の直接挿入に対応済み。 | そのまま C++ ソースコードへ直埋め込み出力（補間部分は変数式を展開）。 | **Tier 1** (45) |
| **SYN-78** | **テンソルコア・WMMA (Warp Matrix Multiply)** | - [ ] | - [ ] | `Wmma.LoadMatrixSync(frag, ptr, stride);`<br>`Wmma.MmaSync(d, a, b, c);` | Tensor Core API の C# 表現未整備。 | CUDA `nvcuda::wmma` API への型安全 C# バインド。 | **Tier 4** (18) |

---

## 12. 最先端・微小な修飾子とスコープ (Micro-Modifiers & Scopes) 【残り5%の領域】

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-79** | **ファイルローカル型 (`file struct`)** | - [ ] | - [ ] | `file struct InternalState { }` | C# 11 アクセス修飾子未対応。 | C++ 無名名前空間 `namespace { struct InternalState { }; }` へ隠蔽展開。 | **Tier 4** (18) |
| **SYN-80** | **`params Span<T>` / `params ReadOnlySpan<T>`** | - [ ] | - [ ] | `void Calc(params ReadOnlySpan<int> vals)` | C# 13 可変長 Span 引数未解決。 | ヒープ確保なしのスタック配列初期化＋C++ 参照渡しへの自動展開。 | **Tier 3** (23) |
| **SYN-81** | **`ref readonly` 戻り値** | - [ ] | - [ ] | `public ref readonly int GetRef()` | 戻り値の参照修飾未展開。 | C++ `const int&` 戻り値への正確なマッピング。 | **Tier 3** (24) |
| **SYN-82** | **`volatile` フィールド修飾子** | - [ ] | - [ ] | `volatile uint syncFlag;` | スレッド間可視性修飾子が無視される。 | CUDA C++ `volatile uint32_t` への付与、または `__threadfence()` 境界の自動挿入。 | **Tier 3** (21) |
| **SYN-83** | **`[module: SkipLocalsInit]` 属性** | - [ ] | - [ ] | `[module: SkipLocalsInit]` | ローカル変数ゼロクリア省略の未評価。 | （C++ ではデフォルトで未初期化のため、特に追加生成コードなしで完了とする設計）。 | **Tier 3** (21) |

---

## 13. 超エッジケース・特殊構文 (Ultra-Niche & Undocumented Syntax) 【残り5%の領域】

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-84** | **タプル宣言分解の `foreach` ループ** | - [ ] | - [ ] | `foreach (var (x, y) in points)` | `ForEachVariableStatementSyntax` 未対応。 | C++17 Structured Binding 組み込みの range-based for `for(auto [x, y] : points)` へ展開。 | **Tier 3** (21) |
| **SYN-86** | **関数ポインタの呼出規約・GC遷移修飾** | - [ ] | - [ ] | `unmanaged[Stdcall, SuppressGCTransition]` | 修飾子属性の解決。 | デバイス側では一律 `__device__` 呼び出し規約として扱うため無視（静的解析で許容）。 | **Tier 4** (18) |
| **SYN-87** | **`#line hidden` / `#line default` ディレクティブ** | - [ ] | - [ ] | `#line hidden` | プリプロセッサディレクティブの無視。 | CUDA デバッガ (cuda-gdb) のステップ実行除外指定へのマッピング。 | **Tier 4** (15) |
| **SYN-88** | **暗黙のスタック配列 (`stackalloc[]`)** | - [ ] | - [ ] | `Span<int> s = stackalloc[] { 1, 2 };` | サイズ推論付き stackalloc の未対応。 | C++ `int s[] = { 1, 2 };` のローカル配列へ展開。 | **Tier 3** (24) |

---

## 15. 基本実装済・検証済コア構文 (Core Syntax & Execution Foundation)

既にフレームワークの初期開発段階で実装・動作確認が完了している基本的な構文および制御構文です。

| ID | 構文・機能 | 実装・動作 | チューニング済 | C# 記述例 | 現状の挙動 / 課題 | C++ (CUDA) 変換先 / 対策方針 | 優先度 (Tier) |
| :--- | :--- | :---: | :---: | :--- | :--- | :--- | :---: |
| **SYN-90** | **複雑な制御フロー (`if-else` ネストとループの複合)** | - [x] | - [x] | `for (...) { if (...) { ... } else { ... } }` | 正常にトランスパイル・実行完了。 | C++ 制御構造へ完全マッピング。 | **Tier 1** (45) |
| **SYN-91** | **基本制御構文 (`switch`, `do-while`, 論理演算 `&&`, `\ | \ | `)** | - [x] | - [x] | `switch(tid) { ... }`<br>`do { ... } while (...);` | **Tier 1** (45) | C++ switch / do-while / 論理積・和へマッピング。 | **Tier 1** (45) |
| **SYN-92** | **ユーザー定義構造体 (`struct`) のカーネル引数および操作** | - [x] | - [x] | `public struct Vector2 { public float X, Y; }` | 正常にトランスパイル・実行完了。 | C++ 構造体型宣言およびフィールドアクセスへマッピング。 | **Tier 1** (45) |
| **SYN-93** | **デバイスメソッド (`[GpuDevice]`) の呼び出し・再利用** | - [x] | - [x] | `[GpuDevice] public static int Add(...)` | 正常にトランスパイル・実行完了。 | C++ `__device__` 修飾子付き関数宣言およびインライン呼出へマッピング。 | **Tier 1** (45) |

---

## 16. G# 永久未対応構文 (Permanently Unsupported Syntax)

アーキテクチャ上の制約（ガベージコレクションや例外機構の欠如、動的ディスパッチの仕様）により、今後も意図的に「対応しない」方針の構文一覧です。これらはAST走査時に即座にビルドエラー（Diagnostic）として弾かれます。

| ID | 構文・機能 | C# 記述例 | 永続的非対応の理由 |
| :--- | :--- | :--- | :--- |
| **UNS-01** | **オブジェクトの生成 (`new`)** | `var obj = new MyClass();` | GPU上にはマネージドヒープやGCが存在しないため。ヒープ確保不可。（※`stackalloc` によるスタック構造体確保や `SharedMemory<T>` は例外的に許可） |
| **UNS-02** | **文字列操作 (`string`)** | `string msg = "Error";` | `string` は参照型（マネージドオブジェクト）であるため。動的確保を伴う文字列表現は不可能です。（※CUDA側の `printf` に展開される `$""` などは別途対応を検討中） |
| **UNS-03** | **LINQ (`System.Linq`)** | `array.Where(x => x > 0)` | 内部でデリゲート生成、イテレータのステートマシン、動的メモリ確保を多用するため、GPUカーネルとは根本的に相性が悪いです。 |
| **UNS-04** | **仮想メソッド / インターフェース** | `public virtual void Run()` | 実行時の動的ディスパッチ（v-table）機構をAOTコンパイラがCUDA上に構築・マッピングできないため。 |
| **UNS-05** | **例外・投擲式 (`throw`, `try-catch`)** | `throw new Exception();`<br>`a ?? throw;` | CUDA C++（デバイスコード）は例外のスタックアンワインド機構（例外のスローと捕捉）を持たないため。`throw`式も含め全て禁止です。 |
| **UNS-06** | **再帰呼び出し (Recursion)** | `int Fib(int n) => Fib(n-1) + Fib(n-2);` | GPUスレッドあたりのスタックサイズは非常に小さく（通常数KB）、再帰呼び出しは即座にスタックオーバーフローを引き起こすため原則禁止です。 |
| **UNS-07** | **リフレクション (Reflection)** | `typeof(T).GetMethods()` | AOTコンパイルされたGPUカーネルには、.NETのメタデータが一切存在しないため。 |
| **UNS-08** | **非同期処理 (`async` / `await`)** | `await Task.Delay(1);` | スレッドのサスペンド・レジュームを行うステートマシン機構をGPUワープ（Warp）上で制御することが現実的ではないため。 |
| **UNS-09** | **隠しキーワード (`__arglist` 等)** | `void Log(__arglist)` | `TypedReference` などCLRマネージド機能に依存する内部構文であり、CUDAのvarargsに安全にマッピングできないため。 |
| **UNS-10** | **デリゲート型とクロージャ (`Func`/`Action`)** | `Func<int> f = () => x;` | ローカル変数キャプチャを伴うクロージャやデリゲート型の生成は、背後でヒープ上にクラスインスタンスを生成するため。（※インラインのラムダ式展開はSYN-08でサポート） |
| **UNS-11** | **`lock` ステートメント** | `lock (obj) { ... }` | `System.Threading.Monitor` とオブジェクトヘッダ（SyncBlock）を利用するため。GPUでの排他制御は `Interlocked` (SYN-49) 等を用いる必要があります。 |
| **UNS-12** | **イテレータ (`yield return`)** | `yield return 1;` | 背後で `IEnumerator` を実装するステートマシンクラスをヒープ上に生成するため。 |
| **UNS-13** | **動的型付け (`dynamic`)** | `dynamic d = obj;` | DLR（動的言語ランタイム）とリフレクションに依存しているため。 |
| **UNS-14** | **イベント (`event`)** | `event Action OnClick;` | マルチキャストデリゲートの生成とヒープ操作を伴うため。 |
