# Phase 10: ゼロアロケーション (stackalloc/unsafe) ＋ JITキャッシュ探索の静的バイパス

C#側のマーシャリングにおける `Marshal.AllocHGlobal` などのヒープ確保を `stackalloc` と `&` (アドレス演算子) に完全に置き換え、
さらにカーネル起動ごとの JITキャッシュハッシュ計算と探索 (`GetOrCompileKernel`) を **C#の静的フィールド (IntPtr) への事前キャッシュ** にバイパスする最適化を導入した。

### 結果 (Phase 10)
| Method           | NumWords | Mean        | Ratio |
|----------------- |--------- |------------:|------:|
| Sub_LegacyNative | 1024     |    21.65 us |  1.00 |
| Sub_GSharpAot    | 1024     |    35.86 us |  1.66 |
|                  |          |             |       |
| Sub_LegacyNative | 4096     |    31.63 us |  1.00 |
| Sub_GSharpAot    | 4096     |    43.05 us |  1.36 |

### 考察
- 1024 words では、前回の `46.24 us` から **`35.86 us`** (Ratio: 2.17x → 1.66x) に高速化。
- 4096 words では、前回の `55.41 us` から **`43.05 us`** (Ratio: 1.77x → 1.36x) に高速化。
- C#側の関数起動オーバーヘッドが約 10~12us 程度削減された。
