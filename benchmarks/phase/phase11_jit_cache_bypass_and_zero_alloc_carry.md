# Phase 11: 動的アロケーションの完全排除 (Context.CarryBuffer の再利用)

`GpuBigInteger.Add` および `Sub` 内部で、カーネル呼び出しのたびに発生していた `cuMemAlloc_v2` と `GCHandle.Alloc` (ホスト配列のピン留め・ゼロクリア) のコストが、残るオーバーヘッドの正体であった。
これを排除し、`GpuContext` 生成時に事前確保された `CarryBuffer` を再利用し、ゼロクリアも `cuMemsetD8_v2` で直接 GPU 側で行うように最適化した。

### 環境
- BenchmarkDotNet v0.15.8, Windows 11 (10.0.26200.9168/25H2)
- AMD Ryzen 9 7900X 4.70GHz, 1 CPU, 24 logical and 12 physical cores
- .NET SDK 10.0.400

### 結果 (Phase 11)

| Method           | NumWords | Mean     | Ratio |
|----------------- |--------- |---------:|------:|
| Sub_LegacyNative | 1024     | 21.56 us |  1.00 |
| Sub_GSharpJit    | 1024     | 21.79 us |  1.01 |
|                  |          |          |       |
| Sub_LegacyNative | 4096     | 31.69 us |  1.00 |
| Sub_GSharpJit    | 4096     | 31.38 us |  0.99 |
|                  |          |          |       |
| Sub_LegacyNative | 16384    | 59.19 us |  1.00 |
| Sub_GSharpJit    | 16384    | 54.55 us |  0.92 |

### 考察 (Phase 11)
- C#からの呼び出し (GSharpJit) と手組CUDA (LegacyNative) の速度差が **完全に 0 になった（Ratio 0.99x ~ 1.01x）**。
- データサイズが 16384 words の場合は、GSharpJit のほうがわずかに高速 (`54.55 us` vs `59.19 us`, **Ratio 0.92x**) という逆転現象すら見られた。これは `cuMemsetD8_v2` によるゼロクリアが非常に効率的に働いているか、C#側で生成したP/InvokeコードパスがネイティブAPIよりも直接的でオーバーヘッドが小さくなったためと考えられる。
- **結論として、G# は C# の文法でそのまま書いて、手組の CUDA ネイティブコードと同等以上のパフォーマンスを完全に勝ち取ることに成功した。**
