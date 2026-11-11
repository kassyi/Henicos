---
title: "GpuBigIntMulKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for large integer multiplication using Number Theoretic Transform (NTT), Comba, and Karatsuba algorithms."
---

## GpuBigIntMulKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Defines CUDA GPU device kernels for large-integer multiplication using a Global Non-Cooperative Number Theoretic Transform (NTT) approach, as well as Comba and Karatsuba algorithms via partial declarations.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntMulKernels
```

## Nested Types

| Name | Description |
| --- | --- |
| [NttUnpackOperandContext](#nttunpackoperandcontext-struct) | One operand to unpack: its source words and its two NTT destination buffers. |
| [NttUnpackContext](#nttunpackcontext-struct) | Two operands to unpack and the six-step NTT grid. |
| [NttUnpackSingleContext](#nttunpacksinglecontext-struct) | One operand to unpack (for a square) and the six-step NTT grid. |

## Methods

| Name | Description |
| --- | --- |
| [MultiplyNttUnpackKernel(NttUnpackContext)](#multiplynttunpackkernelnttunpackcontext) | A GPU kernel (`[GpuKernel]`) that unpacks both operands into dual-modulus NTT buffers. |
| [MultiplyNttUnpackSingleKernel(NttUnpackSingleContext)](#multiplynttunpacksinglekernelnttunpacksinglecontext) | A GPU kernel (`[GpuKernel]`) that unpacks one operand into dual-modulus NTT buffers, for a square. |
| [MultiplyNttSharedRowKernel(NumberTheoreticTransform)](#multiplynttsharedrowkernelnumbertheoretictransform) | A GPU kernel (`[GpuKernel]`) that performs shared-memory row-wise NTT transformations. |
| [MultiplyNttTransposeKernel(uint[], uint[], int, int)](#multiplyntttransposekerneluint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that transposes the NTT matrix using 32x32 shared-memory tiles. |
| [MultiplyNttPointwiseMultiplyKernel(uint[], uint[], uint[], uint[], int)](#multiplynttpointwisemultiplykerneluint-uint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs pointwise modular multiplication in the frequency domain across both NTT moduli. |
| [MultiplyNttPointwiseSquareKernel(uint[], uint[], int)](#multiplynttpointwisesquarekerneluint-uint-int) | A GPU kernel (`[GpuKernel]`) that squares each element in the frequency domain across both NTT moduli. |
| [MultiplyNttComputeCrtKernel(uint[], uint[], uint[], uint[], int, int, int)](#multiplynttcomputecrtkerneluint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that computes the Chinese Remainder Theorem (CRT) coefficients to recombine dual-modulus NTT results. |
| [MultiplyNtt(Span&lt;uint&gt;, Span&lt;uint&gt;, Span&lt;uint&gt;)](#multiplynttspanuint-spanuint-spanuint) | A GPU device function (`[GpuDeviceFunction]`) that multiplies two operands by NTT in the shared memory of one block, when the product fits in 512 words. |
| [MultiplyNtt(Span&lt;uint&gt;, int, Span&lt;uint&gt;, int, Span&lt;uint&gt;, int)](#multiplynttspanuint-int-spanuint-int-spanuint-int) | The same, with explicit operand and result word counts. |
| [MultiplyNttKernel(uint[], int, uint[], int, uint[], int)](#multiplynttkerneluint-int-uint-int-uint-int) | A GPU kernel (`[GpuKernel]`) entry point for the single-block NTT multiplication. |

## NttUnpackOperandContext Struct

One operand to unpack into 16-bit NTT elements: its source words and its destination buffers for the two NTT moduli.

```csharp
public struct NttUnpackOperandContext
{
    [GpuReadOnly] public uint[] Source;
    public uint[] NttBuf1;
    public uint[] NttBuf2;
    public int NumWords;
}
```

## NttUnpackContext Struct

Two operands to unpack and the six-step NTT grid (`NttGridContext`: `M`, `N1`, `N2`).

```csharp
public struct NttUnpackContext
{
    public NttUnpackOperandContext OperandA;
    public NttUnpackOperandContext OperandB;
    public NttGridContext Grid;
}
```

## NttUnpackSingleContext Struct

One operand to unpack, for a square, and the six-step NTT grid.

```csharp
public struct NttUnpackSingleContext
{
    public NttUnpackOperandContext Operand;
    public NttGridContext Grid;
}
```

## MultiplyNttUnpackKernel(NttUnpackContext)

A GPU kernel (`[GpuKernel]`) that unpacks both operands into dual-modulus NTT buffers.

```csharp
[GpuKernel]
public static void MultiplyNttUnpackKernel(NttUnpackContext ctx);
```

#### Parameters

`ctx` _NttUnpackContext_<br>
The two operands (`OperandA`, `OperandB`), each unpacked in the transposed order of the grid `Grid`.

## MultiplyNttUnpackSingleKernel(NttUnpackSingleContext)

A GPU kernel (`[GpuKernel]`) that unpacks one operand into dual-modulus NTT buffers in the transposed order of the grid, for a square.

```csharp
[GpuKernel]
public static void MultiplyNttUnpackSingleKernel(NttUnpackSingleContext ctx);
```

## MultiplyNttSharedRowKernel(NumberTheoreticTransform)

A GPU kernel (`[GpuKernel]`) that performs shared-memory row-wise NTT transformations by delegating to `GpuBigIntNttKernels.ExecuteNttSharedRow`.

```csharp
[GpuKernel]
public static void MultiplyNttSharedRowKernel(NumberTheoreticTransform args);
```

#### Parameters

`args` _NumberTheoreticTransform_<br>
The NTT parameter structure specifying data buffers, twiddle factors, modulus, Barrett magic constant, and row transform dimensions.

## MultiplyNttTransposeKernel(uint[], uint[], int, int)

A GPU kernel (`[GpuKernel]`) that transposes the NTT matrix using 32x32 shared-memory tiles.

```csharp
[GpuKernel]
public static void MultiplyNttTransposeKernel(uint[] input, uint[] output, int width, int height);
```

#### Parameters

`input` _uint[]_<br>
The input matrix buffer.

`output` _uint[]_<br>
The output transposed matrix buffer.

`width` _int_<br>
The width of the input matrix.

`height` _int_<br>
The height of the input matrix.

## MultiplyNttPointwiseMultiplyKernel(uint[], uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that performs pointwise modular multiplication in the frequency domain across both NTT moduli.

```csharp
[GpuKernel]
public static void MultiplyNttPointwiseMultiplyKernel(
    uint[] nttBuf1A, uint[] nttBuf2A, uint[] nttBuf1B, uint[] nttBuf2B, int transformLength);
```

#### Parameters

`nttBuf1A` _uint[]_<br>
The transformed operand A buffer for prime 1. It also stores the product result.

`nttBuf2A` _uint[]_<br>
The transformed operand A buffer for prime 2. It also stores the product result.

`nttBuf1B` _uint[]_<br>
The transformed operand B buffer for prime 1.

`nttBuf2B` _uint[]_<br>
The transformed operand B buffer for prime 2.

`transformLength` _int_<br>
The length of the NTT transform.

## MultiplyNttPointwiseSquareKernel(uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that squares each element of the transformed operand in the frequency domain across both NTT moduli.

```csharp
[GpuKernel]
public static void MultiplyNttPointwiseSquareKernel(uint[] nttBuf1A, uint[] nttBuf2A, int transformLength);
```

## MultiplyNttComputeCrtKernel(uint[], uint[], uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that computes the Chinese Remainder Theorem (CRT) coefficients to recombine dual-modulus NTT results.

```csharp
[GpuKernel]
public static void MultiplyNttComputeCrtKernel(
    uint[] deviceResult1, uint[] deviceResult2, uint[] uncarriedLow, uint[] uncarriedHigh, int transformLength,
    int n1, int n2);
```

#### Parameters

`deviceResult1` _uint[]_<br>
The inverse-NTT result for prime 1.

`deviceResult2` _uint[]_<br>
The inverse-NTT result for prime 2.

`uncarriedLow` _uint[]_<br>
The output buffer for the lower 32 bits of the recombined CRT coefficients.

`uncarriedHigh` _uint[]_<br>
The output buffer for the upper 32 bits of the recombined CRT coefficients.

`transformLength` _int_<br>
The length of the NTT transform.

`n1`, `n2` _int_<br>
The six-step grid dimensions; the coefficients are read in the transposed order of the grid.

<a id="multiplynttspanuint-spanuint-spanuint"></a>

## MultiplyNtt(Span&lt;uint&gt;, Span&lt;uint&gt;, Span&lt;uint&gt;)

A GPU device function (`[GpuDeviceFunction]`) that multiplies `a` by `b` into `result` with a cooperative NTT in the shared memory of one block, for operands whose product fits in 512 32-bit words (1024 16-bit half-words). The word counts are the lengths of the spans.

```csharp
[GpuDeviceFunction]
public static void MultiplyNtt([GpuReadOnly] Span<uint> a, [GpuReadOnly] Span<uint> b, Span<uint> result);
```

<a id="multiplynttspanuint-int-spanuint-int-spanuint-int"></a>

## MultiplyNtt(Span&lt;uint&gt;, int, Span&lt;uint&gt;, int, Span&lt;uint&gt;, int)

The same single-block NTT multiplication with explicit operand and result word counts.

```csharp
[GpuDeviceFunction]
public static void MultiplyNtt(
    [GpuReadOnly] Span<uint> a, int numWordsA,
    [GpuReadOnly] Span<uint> b, int numWordsB,
    Span<uint> result, int numWordsResult);
```

## MultiplyNttKernel(uint[], int, uint[], int, uint[], int)

A GPU kernel (`[GpuKernel]`) entry point for the single-block NTT multiplication; launch it with one block.

```csharp
[GpuKernel]
public static void MultiplyNttKernel(
    uint[] result, int numWordsResult,
    [GpuReadOnly] uint[] a, int numWordsA,
    [GpuReadOnly] uint[] b, int numWordsB);
```

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Algorithms;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// Configure an NttUnpackContext for launching MultiplyNttUnpackKernel via the G# execution pipeline.
var ctx = new GpuBigIntMulKernels.NttUnpackContext
{
    OperandA = new GpuBigIntMulKernels.NttUnpackOperandContext { NumWords = 128 },
    OperandB = new GpuBigIntMulKernels.NttUnpackOperandContext { NumWords = 128 },
    Grid = new NttGridContext { M = 1024, N1 = 32, N2 = 32 }
};
Console.WriteLine($"NttUnpackContext configured: M={ctx.Grid.M}");
```

## Remarks

This class contains `[GpuKernel]` attributed methods. These methods cannot be executed directly on the CPU (Host). They are transpiled to CUDA PTX/SASS by the G# Roslyn Source Generator and launched via the `GpuWorkspace`.

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
