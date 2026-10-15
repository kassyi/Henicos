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

## Fields

| Name | Description |
| --- | --- |
| `Prime1` | The first 32-bit NTT prime modulus (`4194304001`, i.e., $125 \cdot 2^{25} + 1$). |
| `NttG1` | The primitive root (`3`) for `Prime1`. |
| `Prime2` | The second 32-bit NTT prime modulus (`2717908993`, i.e., $81 \cdot 2^{25} + 1$). |
| `NttG2` | The primitive root (`5`) for `Prime2`. |
| `BarrettMagic1` | The Barrett reduction magic constant (`4398046510UL`, $\lfloor 2^{64} / \text{Prime1} \rfloor$) for `Prime1`. |
| `BarrettMagic2` | The Barrett reduction magic constant (`6787108810UL`, $\lfloor 2^{64} / \text{Prime2} \rfloor$) for `Prime2`. |

## Methods

| Name | Description |
| --- | --- |
| [ModAdd(uint, uint, uint)](#modadduint-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that computes modular addition `(operandA + operandB) mod primeModulus`. |
| [ModSub(uint, uint, uint)](#modsubuint-uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that computes modular subtraction `(operandA - operandB) mod primeModulus`. |
| [ModMulBarrett(uint, uint, uint, ulong)](#modmulbarrettuint-uint-uint-ulong) | A GPU device function (`[GpuDeviceFunction]`) that computes modular multiplication using PTX 64-bit wide multiply and Barrett reduction. |
| [ModMul1(uint, uint)](#modmul1uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that computes modular multiplication modulo `Prime1`. |
| [ModMul2(uint, uint)](#modmul2uint-uint) | A GPU device function (`[GpuDeviceFunction]`) that computes modular multiplication modulo `Prime2`. |
| [ModPow(uint, uint, uint, ulong)](#modpowuint-uint-uint-ulong) | A GPU device function (`[GpuDeviceFunction]`) that computes modular exponentiation using binary exponentiation and Barrett reduction. |
| [ExecuteNttSharedRow(NumberTheoreticTransform)](#executenttsharedrownumbertheoretictransform) | A GPU device function (`[GpuDeviceFunction]`) that performs shared-memory row-wise NTT butterfly stages. |
| [ExecuteNttPointwiseSquare(uint[], uint[], int)](#executenttpointwisesquareuint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that squares dual-modulus NTT frequency-domain elements in place. |
| [ExecuteNttPointwiseMultiply(uint[], uint[], uint[], uint[], int)](#executenttpointwisemultiplyuint-uint-uint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that multiplies dual-modulus NTT frequency-domain elements pointwise. |
| [ExecuteCrtComputeCoefficients(uint[], uint[], uint[], uint[], int)](#executecrtcomputecoefficientsuint-uint-uint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that recombines dual-modulus inverse-NTT outputs using the Chinese Remainder Theorem (CRT). |
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

## ModAdd(uint, uint, uint)

A GPU device function (`[GpuDeviceFunction]`) that computes modular addition `(operandA + operandB) mod primeModulus`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ModAdd(uint operandA, uint operandB, uint primeModulus);
```

## ModSub(uint, uint, uint)

A GPU device function (`[GpuDeviceFunction]`) that computes modular subtraction `(operandA - operandB) mod primeModulus`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ModSub(uint operandA, uint operandB, uint primeModulus);
```

## ModMulBarrett(uint, uint, uint, ulong)

A GPU device function (`[GpuDeviceFunction]`) that computes modular multiplication using PTX 64-bit wide multiply and Barrett reduction.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ModMulBarrett(uint operandA, uint operandB, uint primeModulus, ulong barrettMagic);
```

## ModMul1(uint, uint)

A GPU device function (`[GpuDeviceFunction]`) that computes modular multiplication modulo `Prime1`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ModMul1(uint operandA, uint operandB);
```

## ModMul2(uint, uint)

A GPU device function (`[GpuDeviceFunction]`) that computes modular multiplication modulo `Prime2`.

```csharp
[GpuDeviceFunction]
[MethodImpl(MethodImplOptions.AggressiveInlining)]
public static uint ModMul2(uint operandA, uint operandB);
```

## ModPow(uint, uint, uint, ulong)

A GPU device function (`[GpuDeviceFunction]`) that computes modular exponentiation using binary exponentiation and Barrett reduction.

```csharp
[GpuDeviceFunction]
public static uint ModPow(uint baseValue, uint exponent, uint primeModulus, ulong barrettMagic);
```

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
// Inspect the dual 32-bit NTT primes and modular arithmetic helpers.
uint sum = GpuBigIntNttKernels.ModAdd(4194304000u, 5u, GpuBigIntNttKernels.Prime1);
Console.WriteLine($"Prime1={GpuBigIntNttKernels.Prime1}, Prime2={GpuBigIntNttKernels.Prime2}, ModAdd={sum}");
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
