---
title: "GpuBigIntFermatKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernels of the FermatAlgorithm probable-prime test (the fold modulo 2^(2E+1) - 2^E + 11) and shared NTT helpers."
---

## GpuBigIntFermatKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and device functions used by `FermatAlgorithm`: the fold that reduces a square modulo `N = 2^(2E+1) - 2^E + 11 = 2X^2 - X + 11` (`X = 2^E`), and helpers shared with the NTT multiplication (unpacking, shared-memory transposition, shifts).

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntFermatKernels
```

## Nested Types

| Name | Description |
| --- | --- |
| `QuadraticFoldBlockedPassContext` | The buffers and sizes of one fold pass (`QuadraticFoldPassBlockedKernel`). |
| `QuadraticFoldBlockedApplyContext` | The buffers and sizes of the carry application pass (`QuadraticFoldPassApplyKernel`). |
| `QuadraticFoldSliceOffsets` | The word and bit offsets of a fold pass, derived from `m = 2E + 1` and `E`. |

## Methods

| Name | Description |
| --- | --- |
| [DeviceUnpack16(uint[], uint[], uint[], int, int, bool)](#deviceunpack16uint-uint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that unpacks 32-bit words into 16-bit elements across dual NTT buffers. An overload takes the buffers as a `GpuBigIntMulKernels.NttUnpackOperandContext` (`DeviceUnpack16(NttUnpackOperandContext operand, int n, bool syncThreads = true)`). |
| `DeviceUnpack16Transposed(NttUnpackOperandContext, NttGridContext, bool)` | A GPU device function (`[GpuDeviceFunction]`) that unpacks 32-bit words into 16-bit elements in the transposed order of a six-step NTT grid. |
| [DeviceTranspose32X32(uint[], uint[], int, int, bool)](#devicetranspose32x32uint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that transposes a 2D matrix using 32x32 shared-memory tiles with bank-conflict padding. |
| [DeviceShiftLeftParallel(uint[], uint[], int, int, bool)](#deviceshiftleftparalleluint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs a parallel grid-stride left bit-shift across device buffers. |
| [KernelTestBlockAddSubParallel(uint[], uint[], uint[], int, int)](#kerneltestblockaddsubparalleluint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) entry point that invokes `GpuBigIntKernels.DeviceAddSubParallel` for block-level parallel addition or subtraction. |
| [GetQuotientWord(QuadraticFoldBlockedPassContext, QuadraticFoldSliceOffsets, int)](#getquotientwordquadraticfoldblockedpasscontext-quadraticfoldsliceoffsets-int) | A GPU device function (`[GpuDeviceFunction]`) that returns one word of the quotient `source >> m`, and 0 for a negative index. |
| [ExecuteQuadraticFoldPassBlocked(QuadraticFoldBlockedPassContext)](#executequadraticfoldpassblockedquadraticfoldblockedpasscontext) | A GPU device function (`[GpuDeviceFunction]`) that evaluates one fold `r + q * 2^E - t * q` in chunks of one block each. |
| [QuadraticFoldPassBlockedKernel(QuadraticFoldBlockedPassContext)](#quadraticfoldpassblockedkernelquadraticfoldblockedpasscontext) | A GPU kernel (`[GpuKernel]`) that runs one fold pass across all blocks. |
| [QuadraticFoldPassApplyKernel(QuadraticFoldBlockedApplyContext)](#quadraticfoldpassapplykernelquadraticfoldblockedapplycontext) | A GPU kernel (`[GpuKernel]`) that adds the carry of each chunk into the head of the following chunk. |

## QuadraticFoldBlockedPassContext Struct

The buffers and sizes of one fold pass `F(x) = (x mod 2^m) + floor(x / 2^m) * c` modulo `N = 2^m - c` (`m = 2E + 1`, `c = 2^E - ConstantTerm`).

```csharp
public struct QuadraticFoldBlockedPassContext
{
    public uint[] Source;
    public uint[] Result;
    public uint[] BlockCarries;
    public int SourceWords;
    public int ResultWords;
    public int ModulusBits;
    public int Exponent;
    public uint ConstantTerm;
}
```

| Field | Description |
| --- | --- |
| `Source` | The input words of the pass, `SourceWords` words. Not `[GpuReadOnly]`: the second and third folds read what the fold before them wrote. |
| `Result` | Receives the folded value modulo `2^(32 * ResultWords)`, `ResultWords` words. |
| `BlockCarries` | Receives the outgoing carry (`0..4`) of each chunk of `Result`. |
| `ModulusBits`, `Exponent` | `m = 2E + 1` and `E`. |
| `ConstantTerm` | `t` in `c = 2^E - t`: 11. |

## QuadraticFoldBlockedApplyContext Struct

```csharp
public struct QuadraticFoldBlockedApplyContext
{
    public uint[] Result;
    public uint[] BlockCarries;
    public int ResultWords;
    public int ChunkSize;
    public int NumBlocks;
}
```

`Result` and `BlockCarries` are those of the fold pass; `ChunkSize` is the block size of `QuadraticFoldPassBlockedKernel` in words and `NumBlocks` the number of chunks it resolved.

## QuadraticFoldSliceOffsets Struct

```csharp
public struct QuadraticFoldSliceOffsets
{
    public int FoldWord;
    public int FoldBit;
    public int ShiftWord;
    public int ShiftBit;
}
```

## DeviceUnpack16(uint[], uint[], uint[], int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that unpacks 32-bit words into 16-bit elements across dual NTT buffers.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceUnpack16([GpuReadOnly] uint[] dSrc, uint[] dDst1, uint[] dDst2, int n, int numWords, bool syncThreads = true);
```

#### Parameters

`dSrc` _uint[]_<br>
The source 32-bit word buffer.

`dDst1` _uint[]_<br>
The first destination NTT buffer receiving 16-bit unpacked elements.

`dDst2` _uint[]_<br>
The second destination NTT buffer receiving 16-bit unpacked elements.

`n` _int_<br>
The transform length (total number of 16-bit elements).

`numWords` _int_<br>
The number of 32-bit words in `dSrc`.

`syncThreads` _bool_<br>
If `true`, synchronizes threads within the block upon completion.

## DeviceTranspose32X32(uint[], uint[], int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that transposes a 2D matrix using 32x32 shared-memory tiles with bank-conflict padding.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceTranspose32X32([GpuReadOnly] uint[] idata, uint[] odata, int width, int height, bool syncThreads = true);
```

#### Parameters

`idata` _uint[]_<br>
The input matrix buffer of dimensions `height` x `width`.

`odata` _uint[]_<br>
The output transposed matrix buffer of dimensions `width` x `height`.

`width` _int_<br>
The width (column count) of the input matrix.

`height` _int_<br>
The height (row count) of the input matrix.

`syncThreads` _bool_<br>
If `true`, synchronizes threads after each tile write.

## DeviceShiftLeftParallel(uint[], uint[], int, int, bool)

A GPU device function (`[GpuDeviceFunction]`) that performs a parallel grid-stride left bit-shift across device buffers.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void DeviceShiftLeftParallel([GpuReadOnly] uint[] src, uint[] dst, int shiftBits, int numWords, bool syncThreads = true);
```

#### Parameters

`src` _uint[]_<br>
The source word buffer.

`dst` _uint[]_<br>
The destination word buffer.

`shiftBits` _int_<br>
The total number of bits to shift left.

`numWords` _int_<br>
The total number of words to process.

`syncThreads` _bool_<br>
If `true`, synchronizes threads within the block upon completion.

## KernelTestBlockAddSubParallel(uint[], uint[], uint[], int, int)

A GPU kernel (`[GpuKernel]`) entry point that invokes `GpuBigIntKernels.DeviceAddSubParallel` for block-level parallel addition or subtraction.

```csharp
[GpuKernel]
public static void KernelTestBlockAddSubParallel([GpuReadOnly] uint[] a, [GpuReadOnly] uint[] b, uint[] result, int numWords, int sign);
```

#### Parameters

`a` _uint[]_<br>
The first operand buffer.

`b` _uint[]_<br>
The second operand buffer.

`result` _uint[]_<br>
The output result buffer.

`numWords` _int_<br>
The number of 32-bit words to process.

`sign` _int_<br>
`1` for addition (`a + b`), or `-1` for subtraction (`a - b`).

## GetQuotientWord(QuadraticFoldBlockedPassContext, QuadraticFoldSliceOffsets, int)

A GPU device function (`[GpuDeviceFunction]`) that returns word `index` of the quotient `q = Source >> (32 * FoldWord + FoldBit)`, and 0 for a negative index. `GpuBigIntKernels.GetShiftedRightWord` shifts the whole value, so its word -1 holds the top bits of the remainder; as a factor of `q * 2^E` and `t * q` those words must be 0. It reads `Source` through the normal data path, not the read-only one, because a fold reads the words the fold before it wrote in the same launch.

```csharp
[GpuDeviceFunction]
public static uint GetQuotientWord(QuadraticFoldBlockedPassContext ctx, QuadraticFoldSliceOffsets offsets, int index);
```

## ExecuteQuadraticFoldPassBlocked(QuadraticFoldBlockedPassContext)

A GPU device function (`[GpuDeviceFunction]`) that evaluates one fold pass `result = r + q * 2^E - t * q` (`r = Source mod 2^m`, `q = floor(Source / 2^m)`) in parallel chunks of one launch block across all SMs, and writes the outgoing carry (`0..4`) of each chunk to `BlockCarries`. `t * q` is split into two numbers whose words need no carry (the low and the high halves of the 64-bit products `t * q_i`), each subtracted as its complement plus 1.

```csharp
[GpuDeviceFunction]
public static void ExecuteQuadraticFoldPassBlocked(QuadraticFoldBlockedPassContext ctx);
```

## QuadraticFoldPassBlockedKernel(QuadraticFoldBlockedPassContext)

A GPU kernel (`[GpuKernel]`) that runs `ExecuteQuadraticFoldPassBlocked`. Follow it with `QuadraticFoldPassApplyKernel` when `ResultWords` spans more than one block. `FermatAlgorithm` reduces a square modulo `N` with three such passes; the result is again a valid residue below `2N`.

```csharp
[GpuKernel]
public static void QuadraticFoldPassBlockedKernel(QuadraticFoldBlockedPassContext ctx);
```

## QuadraticFoldPassApplyKernel(QuadraticFoldBlockedApplyContext)

A GPU kernel (`[GpuKernel]`) that adds the carry of each chunk, written by `QuadraticFoldPassBlockedKernel`, into the head of the following chunk modulo `2^(32 * ResultWords)`.

```csharp
[GpuKernel]
public static void QuadraticFoldPassApplyKernel(QuadraticFoldBlockedApplyContext ctx);
```

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// The sizes of the first fold pass for E = 1000: m = 2E + 1 = 2001 bits.
const int E = 1000;
const int M = 2 * E + 1;
var ctx = new GpuBigIntFermatKernels.QuadraticFoldBlockedPassContext
{
    ModulusBits = M,
    Exponent = E,
    ConstantTerm = 11,
    SourceWords = 2 * ((M + 31) / 32),
    ResultWords = (M + E + 3 + 31) / 32
};
Console.WriteLine($"Fold pass configured: SourceWords={ctx.SourceWords}, ResultWords={ctx.ResultWords}");
```

## Remarks

This class contains `[GpuKernel]` and `[GpuDeviceFunction]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuWorkspace`.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### BCL API > GPU Class

- [Gpu Class](../../../../03_bcl_api/gpu_class/00_index.md)
- [InlinePtx](../../../../03_bcl_api/gpu_class/InlinePtx.md)

### Guides

- [4. Practical Guides and Best Practices](../../../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
- [7. Stdlib Math and Lazy Evaluation](../../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
