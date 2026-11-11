---
title: "GpuBigIntNttKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Number Theoretic Transform (NTT) core algorithms used in large integer multiplication."
---

## GpuBigIntNttKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains the core GPU kernels, device functions, and prime/Barrett constants required for performing the Number Theoretic Transform (NTT) and Chinese Remainder Theorem (CRT) carry resolution.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntNttKernels
```

The NTT primes, the Barrett constants and the modular arithmetic device functions are members of `NttPrimeArithmetic` (`Prime1`, `Prime2`, `Prime3`, `NttG1`-`NttG3`, `BarrettMagic1`-`BarrettMagic3`, the Garner CRT constants, `ModAdd`, `ModSub`, `ModMulBarrett`, `ModMul`, `ModMul1`-`ModMul3`, `ModReduce64Barrett` and `ModPow`; see [Device Kernels](../00_index.md#device-kernels)). This class uses them through `using static NttPrimeArithmetic`.

## Methods

| Name | Description |
| --- | --- |
| [ExecuteNttSharedRow(NumberTheoreticTransform)](#executenttsharedrownumbertheoretictransform) | A GPU device function (`[GpuDeviceFunction]`) that performs shared-memory row-wise NTT butterfly stages. |
| [ExecuteNttPointwiseSquare(uint[], uint[], int)](#executenttpointwisesquareuint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that squares dual-modulus NTT frequency-domain elements in place. |
| [ExecuteNttPointwiseMultiply(uint[], uint[], uint[], uint[], int)](#executenttpointwisemultiplyuint-uint-uint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that multiplies dual-modulus NTT frequency-domain elements pointwise. |
| [ExecuteCrtComputeCoefficients(uint[], uint[], uint[], uint[], int)](#executecrtcomputecoefficientsuint-uint-uint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that recombines dual-modulus inverse-NTT outputs using the Chinese Remainder Theorem (CRT). |
| [ExecuteCrtComputeCoefficientsTransposed(uint[], uint[], uint[], uint[], int, int, int)](#executecrtcomputecoefficientstransposeduint-uint-uint-uint-int-int-int) | A GPU device function (`[GpuDeviceFunction]`) that computes the CRT coefficients like `ExecuteCrtComputeCoefficients`, reading the inverse-NTT results in the transposed order of an `n1` x `n2` six-step grid. |
| [ExecuteResolveNttCarriesBlocked(uint[], uint[], uint[], uint[], int, int, int)](#executeresolventtcarriesblockeduint-uint-uint-uint-int-int-int) | A GPU device function (`[GpuDeviceFunction]`) that resolves 16-bit NTT convolution carries in a block-wise manner using Kogge-Stone prefix scans. |
| [ResolveNttCarriesBlockedKernel(uint[], uint[], uint[], uint[], int, int, int)](#resolventtcarriesblockedkerneluint-uint-uint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that resolves NTT carries in a block-wise manner. |
| [ExecuteResolveNttCarriesApply(uint[], uint[], int, int, int)](#executeresolventtcarriesapplyuint-uint-int-int-int) | A GPU device function (`[GpuDeviceFunction]`) that applies resolved block carries across chunk boundaries. |
| [ResolveNttCarriesApplyKernel(uint[], uint[], int, int, int)](#resolventtcarriesapplykerneluint-uint-int-int-int) | A GPU kernel (`[GpuKernel]`) that applies resolved NTT carries across blocks. |
| [AtomicAddWithCarry(uint[], int, uint, int)](#atomicaddwithcarryuint-int-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that atomically adds a carry value at a word index and ripples overflow carries upward. |
| [ExecuteForwardNtt(NumberTheoreticTransform)](#executeforwardnttnumbertheoretictransform) | A GPU device function (`[GpuDeviceFunction]`) that executes a complete four-step (Bailey's) or single-block forward NTT. |
| [ExecuteInverseNtt(NumberTheoreticTransform)](#executeinversenttnumbertheoretictransform) | A GPU device function (`[GpuDeviceFunction]`) that executes a complete four-step (Bailey's) or single-block inverse NTT. |
| [ExecuteDualForwardNtt(DualNtt)](#executedualforwardnttdualntt) | A GPU device function (`[GpuDeviceFunction]`) that executes forward NTT transforms for both `Prime1` and `Prime2`. |
| [ExecuteDualInverseNtt(DualNtt)](#executedualinversenttdualntt) | A GPU device function (`[GpuDeviceFunction]`) that executes inverse NTT transforms for both `Prime1` and `Prime2`. |
| [_DiagnosticsWrapper_ExecuteNttSharedRow(uint[])](#_diagnosticswrapper_executenttsharedrowuint) | A diagnostic GPU kernel wrapper (`[GpuKernel]`) used to trigger snapshot transpilation of NTT device functions. |

## ExecuteNttSharedRow(NumberTheoreticTransform)

A GPU device function (`[GpuDeviceFunction]`) that performs shared-memory row-wise NTT butterfly stages.

```csharp
[GpuDeviceFunction]
public static void ExecuteNttSharedRow(NumberTheoreticTransform args);
```

## ExecuteNttPointwiseSquare(uint[], uint[], int)

A GPU device function (`[GpuDeviceFunction]`) that squares dual-modulus NTT frequency-domain elements in place.

```csharp
[GpuDeviceFunction]
public static void ExecuteNttPointwiseSquare(uint[] deviceData1, uint[] deviceData2, int transformLength);
```

## ExecuteNttPointwiseMultiply(uint[], uint[], uint[], uint[], int)

A GPU device function (`[GpuDeviceFunction]`) that multiplies dual-modulus NTT frequency-domain elements pointwise.

```csharp
[GpuDeviceFunction]
public static void ExecuteNttPointwiseMultiply(uint[] deviceData1A, uint[] deviceData2A, uint[] deviceData1B, uint[] deviceData2B, int transformLength);
```

## ExecuteCrtComputeCoefficients(uint[], uint[], uint[], uint[], int)

A GPU device function (`[GpuDeviceFunction]`) that recombines dual-modulus inverse-NTT outputs using the Chinese Remainder Theorem (CRT).

```csharp
[GpuDeviceFunction]
public static void ExecuteCrtComputeCoefficients([GpuReadOnly] uint[] deviceResult1, [GpuReadOnly] uint[] deviceResult2, uint[] uncarriedLow, uint[] uncarriedHigh, int transformLength);
```

## ExecuteCrtComputeCoefficientsTransposed(uint[], uint[], uint[], uint[], int, int, int)

A GPU device function (`[GpuDeviceFunction]`) that recombines the two inverse-NTT results into 64-bit coefficients (`uncarriedLow`, `uncarriedHigh`) like `ExecuteCrtComputeCoefficients`, reading them in the transposed order of an `n1` x `n2` six-step grid.

```csharp
[GpuDeviceFunction]
public static void ExecuteCrtComputeCoefficientsTransposed([GpuReadOnly] uint[] deviceResult1, [GpuReadOnly] uint[] deviceResult2,
    uint[] uncarriedLow, uint[] uncarriedHigh, int transformLength, int n1, int n2);
```

## ExecuteResolveNttCarriesBlocked(uint[], uint[], uint[], uint[], int, int, int)

A GPU device function (`[GpuDeviceFunction]`) that resolves 16-bit NTT convolution carries in a block-wise manner using Kogge-Stone prefix scans.

```csharp
[GpuDeviceFunction]
public static void ExecuteResolveNttCarriesBlocked(uint[] uncarriedLow, uint[] uncarriedHigh, uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize);
```

## ResolveNttCarriesBlockedKernel(uint[], uint[], uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that resolves NTT carries in a block-wise manner.

```csharp
[GpuKernel]
public static void ResolveNttCarriesBlockedKernel(uint[] uncarriedLow, uint[] uncarriedHigh, uint[] outputBuffer, uint[] blockCarries, int transformLength, int maxWordCount, int chunkSize);
```

#### Parameters

`uncarriedLow` _uint[]_<br>
The lower 32 bits of the uncarried CRT coefficients.

`uncarriedHigh` _uint[]_<br>
The upper 32 bits of the uncarried CRT coefficients.

`outputBuffer` _uint[]_<br>
The buffer to store the output after block carries are resolved.

`blockCarries` _uint[]_<br>
The buffer to store intermediate block carry values.

`transformLength` _int_<br>
The length of the NTT transform.

`maxWordCount` _int_<br>
The maximum word count.

`chunkSize` _int_<br>
The size of each chunk processed per block.

## ExecuteResolveNttCarriesApply(uint[], uint[], int, int, int)

A GPU device function (`[GpuDeviceFunction]`) that applies resolved block carries across chunk boundaries.

```csharp
[GpuDeviceFunction]
public static void ExecuteResolveNttCarriesApply(uint[] outputBuffer, uint[] blockCarries, int maxWordCount, int chunkSize, int numBlocks);
```

## ResolveNttCarriesApplyKernel(uint[], uint[], int, int, int)

A GPU kernel (`[GpuKernel]`) that applies resolved NTT carries across blocks.

```csharp
[GpuKernel]
public static void ResolveNttCarriesApplyKernel(uint[] outputBuffer, uint[] blockCarries, int maxWordCount, int chunkSize, int numBlocks);
```

#### Parameters

`outputBuffer` _uint[]_<br>
The output buffer to which block carries are added.

`blockCarries` _uint[]_<br>
The buffer containing the intermediate block carry values.

`maxWordCount` _int_<br>
The maximum word count.

`chunkSize` _int_<br>
The size of each chunk.

`numBlocks` _int_<br>
The total number of blocks.

## AtomicAddWithCarry(uint[], int, uint, int)

A GPU device function (`[GpuDeviceFunction]`) that atomically adds a carry value at a word index and ripples overflow carries upward.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static void AtomicAddWithCarry(uint[] address, int wordIndex, uint valueToAdd, int maxWordCount);
```

## ExecuteForwardNtt(NumberTheoreticTransform)

A GPU device function (`[GpuDeviceFunction]`) that executes a complete four-step (Bailey's) or single-block forward NTT.

```csharp
[GpuDeviceFunction]
public static void ExecuteForwardNtt(NumberTheoreticTransform args);
```

## ExecuteInverseNtt(NumberTheoreticTransform)

A GPU device function (`[GpuDeviceFunction]`) that executes a complete four-step (Bailey's) or single-block inverse NTT.

```csharp
[GpuDeviceFunction]
public static void ExecuteInverseNtt(NumberTheoreticTransform args);
```

## ExecuteDualForwardNtt(DualNtt)

A GPU device function (`[GpuDeviceFunction]`) that executes forward NTT transforms for both `Prime1` and `Prime2`.

```csharp
[GpuDeviceFunction]
public static void ExecuteDualForwardNtt(DualNtt ctx);
```

## ExecuteDualInverseNtt(DualNtt)

A GPU device function (`[GpuDeviceFunction]`) that executes inverse NTT transforms for both `Prime1` and `Prime2`.

```csharp
[GpuDeviceFunction]
public static void ExecuteDualInverseNtt(DualNtt ctx);
```

## _DiagnosticsWrapper_ExecuteNttSharedRow(uint[])

A diagnostic GPU kernel wrapper (`[GpuKernel]`) used to trigger snapshot transpilation of NTT device functions.

```csharp
[GpuKernel]
public static void _DiagnosticsWrapper_ExecuteNttSharedRow(uint[] data);
```

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// Inspect the 32-bit NTT primes and modular arithmetic helpers that GpuBigIntNttKernels uses.
uint sum = NttPrimeArithmetic.ModAdd(4194304000u, 5u, NttPrimeArithmetic.Prime1);
Console.WriteLine($"Prime1={NttPrimeArithmetic.Prime1}, Prime2={NttPrimeArithmetic.Prime2}, ModAdd={sum}");
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
