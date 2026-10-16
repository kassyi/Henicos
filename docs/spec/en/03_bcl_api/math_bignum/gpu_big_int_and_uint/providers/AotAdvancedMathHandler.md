---
title: "AotAdvancedMathHandler Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Dispatches advanced arbitrary-precision arithmetic operations (NTT multiplication, Lehmer GCD, ModInverse, and Newton-Raphson mega-kernels) to GPU device kernels."
---

## AotAdvancedMathHandler Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Implements [`IGpuAdvancedMathProvider`](IGpuExecutionProvider.md) by dispatching high-complexity multi-precision arithmetic operations—including four-step NTT convolution, Lehmer GCD macro-kernels, Extended Binary GCD modular inversion, and cooperative Newton-Raphson reciprocal/inverse-square-root mega-kernels—to compiled G# CUDA kernels.

```csharp
public sealed class AotAdvancedMathHandler : IGpuAdvancedMathProvider
```

## Methods

| Name | Description |
| --- | --- |
| [Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)](#mulgpuworkspace-gpubiguint-uint32-gpubiguint) | Launches `GpuBigIntScalarKernels.ScalarMulKernel` to multiply a multi-word operand by a 32-bit unsigned scalar. |
| [Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#mulgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Executes global non-cooperative dual-modulus NTT multiplication and CRT carry resolution on the GPU. |
| [Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#gcdgpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the Greatest Common Divisor using `GpuBigIntGcdKernels.ExecuteLehmerGcdMacroKernel` and `GpuBigIntDivRem.Mod` fallback steps. |
| [ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)](#modinversegpuworkspace-gpubiguint-gpubiguint-gpubiguint) | Computes the modular multiplicative inverse via `GpuBigIntGcdKernels.ExecuteExtendedBinaryGcdMacroKernel` (for odd moduli) or `ModInverseHostLoopController` (for even moduli). |
| [InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)](#inversesqrtmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32) | Launches `GpuBigIntSqrtKernels.InverseSqrtNewtonRaphsonMegaKernel` to compute the fixed-point inverse square root on the GPU. |
| [ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)](#reciprocalmegagpuworkspace-gpubiguint-gpubiguint-int32-int32-int32-int32-int32) | Launches `GpuBigIntDivRemKernels.ReciprocalNewtonRaphsonMegaKernel` to compute the fixed-point reciprocal on the GPU. |
| [ExecuteModPowNttMegaKernel(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt, Int32, Int32, UInt32, UInt32)](#executemodpownttmegakernelgpuworkspace-gpubiguint-gpubiguint-gpubiguint-gpubiguint-gpubiguint-int32-int32-uint32-uint32) | Executes chunked `GpuBigIntDivRemKernels.ModPowNttMegaKernel` launches with per-chunk stream synchronization to prevent TDR watchdog timeouts. |

## Mul(GpuWorkspace, GpuBigUInt, UInt32, GpuBigUInt)

Launches `GpuBigIntScalarKernels.ScalarMulKernel` on a single 256-thread block (`gridDim = 1, blockDim = 256`) to multiply a multi-word operand by a 32-bit unsigned scalar.

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, uint rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The multi-word unsigned big integer multiplicand.

`rightOperand` _UInt32_<br>
The 32-bit unsigned scalar multiplier.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand * rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when the kernel launch fails.

## Mul(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Executes global non-cooperative dual-modulus Number Theoretic Transform (NTT) convolution (`P1 = 2013265921`, `P2 = 1811939329`) followed by Chinese Remainder Theorem (CRT) reconstruction and stream-compacted carry propagation on the GPU.

```csharp
public void Mul(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary NTT buffers and the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The left unsigned big integer multiplicand.

`rightOperand` _GpuBigUInt_<br>
The right unsigned big integer multiplier.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving `leftOperand * rightOperand`.

#### Exceptions

`CudaException`<br>
Thrown when NTT twiddle allocation, kernel execution, or carry propagation fails.

## Gcd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the Greatest Common Divisor ($\gcd(a, b)$) on the GPU by iterating `GpuBigIntGcdKernels.ExecuteLehmerGcdMacroKernel` and falling back to full `GpuBigIntDivRem.Mod` reduction when Lehmer matrix progress stalls.

```csharp
public void Gcd(GpuWorkspace workspace, GpuBigUInt leftOperand, GpuBigUInt rightOperand, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`leftOperand` _GpuBigUInt_<br>
The first unsigned big integer operand $a$.

`rightOperand` _GpuBigUInt_<br>
The second unsigned big integer operand $b$.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving $\gcd(a, b)$.

#### Exceptions

`CudaException`<br>
Thrown when kernel execution or device memory copying fails.

## ModInverse(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)

Computes the modular multiplicative inverse $a^{-1} \bmod m$ via `GpuBigIntGcdKernels.ExecuteExtendedBinaryGcdMacroKernel` when `modulus` is odd, or `ModInverseHostLoopController.Execute` when `modulus` is even.

```csharp
public unsafe void ModInverse(GpuWorkspace workspace, GpuBigUInt value, GpuBigUInt modulus, GpuBigUInt resultBuffer);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary buffers and the active CUDA stream.

`value` _GpuBigUInt_<br>
The operand $a$ to invert modulo $m$.

`modulus` _GpuBigUInt_<br>
The modulus $m$.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer receiving $a^{-1} \bmod m$.

#### Exceptions

`ArithmeticException`<br>
Thrown when `value` and `modulus` are not coprime ($\gcd(a, m) \ne 1$).

`Exception`<br>
Thrown if `ExecuteExtendedBinaryGcdMacroKernel` fails to converge within the maximum iteration bound (`maxWords * 128 + 1024`).

`CudaException`<br>
Thrown when kernel execution or device memory allocation fails.

## InverseSqrtMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32)

Launches `GpuBigIntSqrtKernels.InverseSqrtNewtonRaphsonMegaKernel` to compute the fixed-point inverse square root on the GPU using cooperative grid-wide synchronization and NTT polynomial multiplication.

```csharp
public void InverseSqrtMega(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    int marginWords,
    int actualBits,
    int iterations);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary NTT and scratch buffers.

`targetOperand` _GpuBigUInt_<br>
The input unsigned big integer operand.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer initialized with the seed approximation and receiving the refined inverse square root.

`marginWords` _Int32_<br>
The total word capacity including guard words (`n`).

`actualBits` _Int32_<br>
The bit length of `targetOperand`.

`iterations` _Int32_<br>
The number of Newton-Raphson refinement iterations to execute inside the mega-kernel.

#### Exceptions

`CudaException`<br>
Thrown when cooperative kernel launch or NTT scratch allocation fails.

## ReciprocalMega(GpuWorkspace, GpuBigUInt, GpuBigUInt, Int32, Int32, Int32, Int32, Int32)

Launches `GpuBigIntDivRemKernels.ReciprocalNewtonRaphsonMegaKernel` to compute the fixed-point reciprocal on the GPU using cooperative grid-wide synchronization and NTT polynomial multiplication.

```csharp
public void ReciprocalMega(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    int marginWords,
    int numBits,
    int marginBits,
    int iterations,
    int targetBits);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary NTT and scratch buffers.

`targetOperand` _GpuBigUInt_<br>
The divisor operand whose fixed-point reciprocal is computed.

`resultBuffer` _GpuBigUInt_<br>
The destination buffer initialized with the seed approximation and receiving the refined reciprocal.

`marginWords` _Int32_<br>
The total word capacity including guard words (`n`).

`numBits` _Int32_<br>
The bit length of the divisor (`dBits`).

`marginBits` _Int32_<br>
The extra guard bits added to avoid truncation drift during Newton-Raphson iterations.

`iterations` _Int32_<br>
The number of Newton-Raphson refinement iterations to execute inside the mega-kernel.

`targetBits` _Int32_<br>
The target fixed-point fractional bit precision.

#### Exceptions

`CudaException`<br>
Thrown when cooperative kernel launch or NTT scratch allocation fails.

## ExecuteModPowNttMegaKernel(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt, GpuBigUInt, Int32, Int32, UInt32, UInt32)

Executes `GpuBigIntDivRemKernels.ModPowNttMegaKernel` in 8-word (256-bit) exponent chunks with per-chunk stream synchronization to prevent OS Timeout Detection and Recovery (TDR) watchdog resets during long modular exponentiations.

```csharp
public void ExecuteModPowNttMegaKernel(
    GpuWorkspace workspace,
    GpuBigUInt targetOperand,
    GpuBigUInt resultBuffer,
    GpuBigUInt constant,
    GpuBigUInt reciprocalMod,
    GpuBigUInt exponent,
    int n,
    int targetBits,
    uint gridDim,
    uint blockDim);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing temporary NTT and Barrett reduction scratch buffers.

`targetOperand` _GpuBigUInt_<br>
The base operand buffer (updated across chunks).

`resultBuffer` _GpuBigUInt_<br>
The accumulator buffer receiving $base^{exponent} \bmod m$.

`constant` _GpuBigUInt_<br>
The modulus $m$.

`reciprocalMod` _GpuBigUInt_<br>
The precomputed Newton-Raphson reciprocal of `constant`.

`exponent` _GpuBigUInt_<br>
The multi-word exponent buffer.

`n` _Int32_<br>
The word capacity of the operands.

`targetBits` _Int32_<br>
The target fixed-point fractional bit precision used by the Barrett reduction step.

`gridDim` _UInt32_<br>
The cooperative 1D grid dimension.

`blockDim` _UInt32_<br>
The 1D thread block dimension.

#### Exceptions

`CudaException`<br>
Thrown when cooperative kernel execution or stream synchronization fails.

## Remarks

`AotAdvancedMathHandler` caches precomputed forward and inverse NTT twiddle factor tables in device memory per transform length $m$ (`ConcurrentDictionary<int, Lazy<TwiddleCache>>`) to avoid redundant host-side root-of-unity generation across repeated multiplications.

## Examples

### Instantiating AotAdvancedMathHandler

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: Host (CPU)
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

IGpuAdvancedMathProvider handler = new AotAdvancedMathHandler();
Console.WriteLine($"Initialized handler: {handler.GetType().Name}");
```

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
