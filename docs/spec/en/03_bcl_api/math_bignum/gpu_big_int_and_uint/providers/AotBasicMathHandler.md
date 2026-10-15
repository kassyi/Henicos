---
title: "AotBasicMathHandler Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Dispatches elementary GPU BigInteger arithmetic, bitwise logic, and comparison operations to compiled GSharp device kernels."
---

## AotBasicMathHandler Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Implements `IGpuBasicMathProvider` by dispatching elementary multi-precision addition, subtraction, scalar addition/subtraction, bit-shifts, bitwise logic, and comparison kernels to compiled G# CUDA kernels.

```csharp
public sealed class AotBasicMathHandler : IGpuBasicMathProvider
```

## Methods

| Name | Description |
| --- | --- |
| `Add(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Launches `GpuBigIntKernels.ParallelAddKernel` for single-block Kogge-Stone parallel addition. |
| `Sub(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Launches `GpuBigIntKernels.ParallelSubKernel` for single-block Kogge-Stone parallel subtraction. |
| `Add(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)` | Launches `GpuBigIntScalarKernels.ScalarAddKernel` for parallel scalar addition. |
| `Sub(GpuWorkspace, GpuBigUInt, uint, GpuBigUInt)` | Launches `GpuBigIntScalarKernels.ScalarSubKernel` for parallel scalar subtraction. |
| `BitwiseAnd(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Launches `GpuBigIntKernels.BitwiseAndKernel` across a 1D grid. |
| `BitwiseOr(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Launches `GpuBigIntKernels.BitwiseOrKernel` across a 1D grid. |
| `BitwiseXor(GpuWorkspace, GpuBigUInt, GpuBigUInt, GpuBigUInt)` | Launches `GpuBigIntKernels.BitwiseXorKernel` across a 1D grid. |
| `BitwiseNot(GpuWorkspace, GpuBigUInt, GpuBigUInt)` | Launches `GpuBigIntKernels.BitwiseNotKernel` across a 1D grid. |
| `ShiftLeft(GpuWorkspace, GpuBigUInt, int, GpuBigUInt)` | Launches `GpuBigIntKernels.ShiftLeftKernel` with decomposed word and bit offsets. |
| `ShiftRight(GpuWorkspace, GpuBigUInt, int, GpuBigUInt)` | Launches `GpuBigIntKernels.ShiftRightKernel` with decomposed word and bit offsets. |
| `Square(GpuWorkspace, IntPtr, int)` | Computes in-place squaring by delegating to `GpuBigIntMath.Mul`. |
| `IsZero(GpuWorkspace, GpuBigUInt)` | Evaluates `GpuBigIntCmpKernels.IsZeroKernel` and returns `true` if all words are zero. |
| `IsOne(GpuWorkspace, GpuBigUInt)` | Evaluates `GpuBigIntCmpKernels.IsOneKernel` and returns `true` if the operand equals `1`. |
| `GetBitLength(GpuWorkspace, GpuBigUInt)` | Evaluates `GpuBigIntCmpKernels.GetBitLengthKernel` and returns the significant bit length. |
| `CompareTo(GpuWorkspace, GpuBigUInt, GpuBigUInt)` | Evaluates `GpuBigIntCmpKernels.CompareToKernel` (zero-padding mismatched word counts if necessary) and returns `-1`, `0`, or `1`. |

## Examples

### Instantiating AotBasicMathHandler

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Providers.Handlers;

IGpuBasicMathProvider handler = new AotBasicMathHandler();
Console.WriteLine($"Initialized handler: {handler.GetType().Name}");
```

## Remarks

`AotBasicMathHandler` is used internally by [`GSharpAotProvider`](GSharpAotProvider.md) to separate elementary arithmetic kernel launches from memory management and CUDA Graph orchestration.

## See also

### BCL API > Math (BigNum)

- [Arbitrary-Precision Integers (GpuBigInt)](../00_index.md)
- [GSharpAotProvider](GSharpAotProvider.md)
- [IGpuExecutionProvider](IGpuExecutionProvider.md)
