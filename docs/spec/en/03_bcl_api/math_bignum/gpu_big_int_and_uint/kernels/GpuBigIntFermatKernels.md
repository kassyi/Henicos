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
| `QuadraticFoldContext` | The buffers and sizes of `QuadraticFoldReduceKernel`. |

## Methods

| Name | Description |
| --- | --- |
| [DeviceUnpack16(uint[], uint[], uint[], int, int, bool)](#deviceunpack16uint-uint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that unpacks 32-bit words into 16-bit elements across dual NTT buffers. |
| [DeviceTranspose32X32(uint[], uint[], int, int, bool)](#devicetranspose32x32uint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that transposes a 2D matrix using 32x32 shared-memory tiles with bank-conflict padding. |
| [DeviceShiftLeftParallel(uint[], uint[], int, int, bool)](#deviceshiftleftparalleluint-uint-int-int-bool) | A GPU device function (`[GpuDeviceFunction]`) that performs a parallel grid-stride left bit-shift across device buffers. |
| [KernelTestBlockAddSubParallel(uint[], uint[], uint[], int, int)](#kerneltestblockaddsubparalleluint-uint-uint-int-int) | A GPU kernel (`[GpuKernel]`) entry point that invokes `GpuBigIntKernels.DeviceAddSubParallel` for block-level parallel addition or subtraction. |
| [GetQuotientWord(uint[], int, int, int, int)](#getquotientworduint-int-int-int-int) | A GPU device function (`[GpuDeviceFunction]`) that returns one word of the quotient `source >> m`, and 0 for a negative index. |
| [QuadraticFoldPass(uint[], int, uint[], int, int, int, uint)](#quadraticfoldpassuint-int-uint-int-int-int-uint) | A GPU device function (`[GpuDeviceFunction]`) that performs one fold `r + q * 2^E - t * q` with one block. |
| [QuadraticFoldReduceKernel(QuadraticFoldContext)](#quadraticfoldreducekernelquadraticfoldcontext) | A GPU kernel (`[GpuKernel]`) that reduces the square of a residue modulo `N` with three folds. |

## QuadraticFoldContext Struct

The buffers and sizes of `QuadraticFoldReduceKernel`, which reduces the square of a residue modulo `N = 2^m - c` (`m = 2E + 1`, `c = 2^E - ConstantTerm`).

```csharp
public struct QuadraticFoldContext
{
    public uint[] Product;
    public uint[] FirstFold;
    public uint[] SecondFold;
    public uint[] Residue;
    public int ProductWords;
    public int FirstFoldWords;
    public int SecondFoldWords;
    public int ResidueWords;
    public int ModulusBits;
    public int Exponent;
    public uint ConstantTerm;
}
```

| Field | Description |
| --- | --- |
| `Product` | The square to reduce, `ProductWords` (`2 * ceil(m / 32)`) words. |
| `FirstFold`, `SecondFold` | The outputs of the first two folds: `m + E + 3` and `m + 2` bits. |
| `Residue` | Receives the reduced value, `ResidueWords` (`ceil(m / 32)`) words. It may be the buffer that was squared. |
| `ModulusBits`, `Exponent` | `m = 2E + 1` and `E`. |
| `ConstantTerm` | `t` in `c = 2^E - t`: 11. |

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

## GetQuotientWord(uint[], int, int, int, int)

A GPU device function (`[GpuDeviceFunction]`) that returns word `index` of the quotient `q = source >> (32 * foldWord + foldBit)`, and 0 for a negative index. `GpuBigIntKernels.GetShiftedRightWord` shifts the whole value, so its word -1 holds the top bits of the remainder; as a factor of `q * 2^E` and `t * q` those words must be 0.

```csharp
[GpuDeviceFunction]
public static uint GetQuotientWord(uint[] source, int sourceWords, int index, int foldWord, int foldBit);
```

## QuadraticFoldPass(uint[], int, uint[], int, int, int, uint)

A GPU device function (`[GpuDeviceFunction]`) run by one block: `result = r + q * 2^E - t * q` with `r = source mod 2^m` and `q = floor(source / 2^m)`, modulo `2^(32 * resultWords)`. `t * q` is subtracted as two carry-free numbers (the low and high halves of the 64-bit products `t * q_i`) in two's complement; carries are resolved per chunk of one block with a warp and block scan.

```csharp
[GpuDeviceFunction]
public static void QuadraticFoldPass(uint[] source, int sourceWords, uint[] result, int resultWords, int modulusBits, int exponent, uint constantTerm);
```

## QuadraticFoldReduceKernel(QuadraticFoldContext)

A GPU kernel (`[GpuKernel]`) that reduces the square of a residue modulo `N = 2^m - c` with three folds `F(x) = (x mod 2^m) + floor(x / 2^m) * c`. For a residue `u < 2^(m+1)` the result is congruent to `u^2` and below `2^m + 2^(E+1) <= 2N`, so it is again a valid residue, and one conditional subtraction of `N` makes it canonical.

```csharp
[GpuKernel]
public static void QuadraticFoldReduceKernel(QuadraticFoldContext ctx);
```

Launch it with one block (`gridDimX = 1`) of a multiple of 32 threads, at most 1024. The host reference of the kernel is `QuadraticFoldModulus.FoldReference` (internal), which the CPU fallback of `FermatAlgorithm` uses.

#### Parameters

`ctx` _QuadraticFoldContext_<br>
The product, the two fold temporaries, the residue and the sizes.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// The sizes of QuadraticFoldReduceKernel for E = 1000: m = 2E + 1 = 2001 bits.
const int E = 1000;
const int M = 2 * E + 1;
var ctx = new GpuBigIntFermatKernels.QuadraticFoldContext
{
    ModulusBits = M,
    Exponent = E,
    ConstantTerm = 11,
    ResidueWords = (M + 31) / 32,
    ProductWords = 2 * ((M + 31) / 32),
    FirstFoldWords = (M + E + 3 + 31) / 32,
    SecondFoldWords = (M + 2 + 31) / 32
};
Console.WriteLine($"QuadraticFoldContext configured: ResidueWords={ctx.ResidueWords}, FirstFoldWords={ctx.FirstFoldWords}");
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
