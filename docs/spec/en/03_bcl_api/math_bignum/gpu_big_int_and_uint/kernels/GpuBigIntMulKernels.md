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
| [NttUnpackContext](#nttunpackcontext-struct) | Encapsulates operand buffers, dual-modulus NTT destination buffers, and length parameters for unpacking 32-bit limbs into 16-bit NTT elements. |

## Methods

| Name | Description |
| --- | --- |
| [MultiplyNttUnpackKernel(NttUnpackContext)](#multiplynttunpackkernelnttunpackcontext) | A GPU kernel (`[GpuKernel]`) that unpacks both operands into dual-modulus NTT buffers. |
| [MultiplyNttSharedRowKernel(NumberTheoreticTransform)](#multiplynttsharedrowkernelnumbertheoretictransform) | A GPU kernel (`[GpuKernel]`) that performs shared-memory row-wise NTT transformations. |
| [MultiplyNttTransposeKernel(uint[], uint[], int, int)](#multiplyntttransposekerneluint-uint-int-int) | A GPU kernel (`[GpuKernel]`) that transposes the NTT matrix using 32x32 shared-memory tiles. |
| [MultiplyNttPointwiseMultiplyKernel(uint[], uint[], uint[], uint[], int)](#multiplynttpointwisemultiplykerneluint-uint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that performs pointwise modular multiplication in the frequency domain across both NTT moduli. |
| [MultiplyNttComputeCrtKernel(uint[], uint[], uint[], uint[], int)](#multiplynttcomputecrtkerneluint-uint-uint-uint-int) | A GPU kernel (`[GpuKernel]`) that computes the Chinese Remainder Theorem (CRT) coefficients to recombine dual-modulus NTT results. |

## NttUnpackContext Struct

Encapsulates operand buffers, dual-modulus NTT destination buffers, and length parameters for unpacking 32-bit limbs into 16-bit NTT elements.

```csharp
public struct NttUnpackContext
{
    [GpuReadOnly] public uint[] OpA;
    [GpuReadOnly] public uint[] OpB;
    public uint[] NttBuf1A;
    public uint[] NttBuf2A;
    public uint[] NttBuf1B;
    public uint[] NttBuf2B;
    public int TransformLength;
    public int NumWordsA;
    public int NumWordsB;
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
The unpack context containing source operand buffers (`OpA`, `OpB`), destination NTT buffers (`NttBuf1A`, `NttBuf2A`, `NttBuf1B`, `NttBuf2B`), `TransformLength`, `NumWordsA`, and `NumWordsB`.

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

## MultiplyNttComputeCrtKernel(uint[], uint[], uint[], uint[], int)

A GPU kernel (`[GpuKernel]`) that computes the Chinese Remainder Theorem (CRT) coefficients to recombine dual-modulus NTT results.

```csharp
[GpuKernel]
public static void MultiplyNttComputeCrtKernel(
    uint[] deviceResult1, uint[] deviceResult2, uint[] uncarriedLow, uint[] uncarriedHigh, int transformLength);
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

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// Configure an NttUnpackContext for launching MultiplyNttUnpackKernel via the G# execution pipeline.
var ctx = new GpuBigIntMulKernels.NttUnpackContext
{
    TransformLength = 1024,
    NumWordsA = 128,
    NumWordsB = 128
};
Console.WriteLine($"NttUnpackContext configured: TransformLength={ctx.TransformLength}");
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
