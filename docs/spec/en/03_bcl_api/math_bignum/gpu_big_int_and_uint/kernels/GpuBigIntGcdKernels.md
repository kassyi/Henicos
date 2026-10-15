---
title: "GpuBigIntGcdKernels Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "CUDA kernel implementations for Greatest Common Divisor (GCD) algorithms."
---

## GpuBigIntGcdKernels Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Contains native G# (CUDA) kernels and device functions used for Greatest Common Divisor (GCD) and modular inverse computations, including Lehmer's GCD matrix reductions and Extended Binary GCD on the GPU.

```csharp
[SkipSnapshotConsistency("Tested in separate suites or WIP")]
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntGcdKernels
```

## Nested Types

| Name | Description |
| --- | --- |
| `AotContexts` | Container class defining unmanaged pointer context structures for GCD kernels (`ExtendedBinaryGcdContext`, `LehmerGcdContext`, `Matrix2x2ApplyContext`). |
| `AotContexts.ExtendedBinaryGcdContext` | Unmanaged context structure for the Extended Binary GCD macro kernel. |
| `AotContexts.LehmerGcdContext` | Unmanaged context structure for the Lehmer GCD macro kernel. |
| `AotContexts.Matrix2x2ApplyContext` | Unmanaged context structure for applying a 2x2 Lehmer reduction matrix to two multi-word integers. |

## Methods

| Name | Description |
| --- | --- |
| [ApplyMatrix2x2Kernel(AotContexts.Matrix2x2ApplyContext)](#applymatrix2x2kernelaotcontextsmatrix2x2applycontext) | A GPU kernel (`[GpuKernel]`) that applies a 2x2 reduction matrix to a pair of large integers. |
| [ComputeLehmerMatrix(ulong, ulong, out long, out long, out long, out long)](#computelehmermatrixulong-ulong-out-long-out-long-out-long-out-long) | A GPU device function (`[GpuDeviceFunction]`) that computes the 2x2 Lehmer reduction matrix from the top 64-bit approximations of two operands. |
| [ExecuteLehmerGcdMacroKernel(AotContexts.LehmerGcdContext)](#executelehmergcdmacrokernelaotcontextslehmergcdcontext) | A GPU kernel (`[GpuKernel]`) that executes multi-iteration Lehmer GCD reduction steps on the device. |
| [ExecuteExtendedBinaryGcdMacroKernel(AotContexts.ExtendedBinaryGcdContext)](#executeextendedbinarygcdmacrokernelaotcontextsextendedbinarygcdcontext) | A GPU kernel (`[GpuKernel]`) that executes the Extended Binary GCD algorithm on the device for modular inversion. |

## AotContexts Class

Defines unmanaged pointer context structures passed to GCD kernels.

```csharp
public static partial class AotContexts
{
    public unsafe struct ExtendedBinaryGcdContext
    {
        public uint* M;
        public uint* U;
        public uint* V;
        public uint* X1;
        public uint* X2;
        public int* Status;
        public int NumWords;
        public int MaxIterations;
    }

    public unsafe struct LehmerGcdContext
    {
        public uint* A;
        public uint* B;
        public uint* NewA;
        public uint* NewB;
        public int* Status;
        public int NumWords;
        public int MaxIterations;
    }

    public unsafe struct Matrix2x2ApplyContext
    {
        [GpuReadOnly] public uint* A;
        [GpuReadOnly] public uint* B;
        public uint* DstA;
        public uint* DstB;
        public long U0;
        public long V0;
        public long U1;
        public long V1;
        public int NumWords;
    }
}
```

## ApplyMatrix2x2Kernel(AotContexts.Matrix2x2ApplyContext)

A GPU kernel (`[GpuKernel]`) that applies a 2x2 reduction matrix to a pair of large integers.

```csharp
[GpuKernel]
public static void ApplyMatrix2x2Kernel(AotContexts.Matrix2x2ApplyContext ctx);
```

#### Parameters

`ctx` _AotContexts.Matrix2x2ApplyContext_<br>
The context structure containing source pointers (`A`, `B`), destination pointers (`DstA`, `DstB`), 2x2 matrix coefficients (`U0`, `V0`, `U1`, `V1`), and `NumWords`.

## ComputeLehmerMatrix(ulong, ulong, out long, out long, out long, out long)

A GPU device function (`[GpuDeviceFunction]`) that computes the 2x2 Lehmer reduction matrix from the top 64-bit approximations of two operands using Knuth's Algorithm L single-crossover condition.

```csharp
[GpuDeviceFunction]
public static bool ComputeLehmerMatrix(ulong x, ulong y, out long u0, out long v0, out long u1, out long v1);
```

#### Parameters

`x` _ulong_<br>
The high 64-bit approximation of the first operand.

`y` _ulong_<br>
The high 64-bit approximation of the second operand.

`u0` _long_<br>
When this method returns, contains the matrix element at row 0, column 0.

`v0` _long_<br>
When this method returns, contains the matrix element at row 0, column 1.

`u1` _long_<br>
When this method returns, contains the matrix element at row 1, column 0.

`v1` _long_<br>
When this method returns, contains the matrix element at row 1, column 1.

#### Returns

_bool_<br>
`true` if at least one Euclidean reduction step succeeded before crossover; otherwise, `false`.

## ExecuteLehmerGcdMacroKernel(AotContexts.LehmerGcdContext)

A GPU kernel (`[GpuKernel]`) that executes multi-iteration Lehmer GCD reduction steps on the device until full-precision modular reduction is required or completion is reached.

```csharp
[GpuKernel]
public static void ExecuteLehmerGcdMacroKernel(AotContexts.LehmerGcdContext ctx);
```

#### Parameters

`ctx` _AotContexts.LehmerGcdContext_<br>
The context structure containing operand pointers, scratch buffers, status pointer, word count, and maximum iteration count.

## ExecuteExtendedBinaryGcdMacroKernel(AotContexts.ExtendedBinaryGcdContext)

A GPU kernel (`[GpuKernel]`) that executes the Extended Binary GCD algorithm on the device to compute modular inverses.

```csharp
[GpuKernel]
public static void ExecuteExtendedBinaryGcdMacroKernel(AotContexts.ExtendedBinaryGcdContext ctx);
```

#### Parameters

`ctx` _AotContexts.ExtendedBinaryGcdContext_<br>
The context structure containing modulus pointer `M`, working buffers `U`, `V`, `X1`, `X2`, status pointer, word count, and maximum iteration count.

## Examples

### Internal Kernel Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

// Execution Context: G# Kernel (GPU)
// Configure a Matrix2x2ApplyContext for launching ApplyMatrix2x2Kernel via the G# execution pipeline.
var ctx = new GpuBigIntGcdKernels.AotContexts.Matrix2x2ApplyContext
{
    U0 = 1,
    V0 = 0,
    U1 = 0,
    V1 = 1,
    NumWords = 64
};
Console.WriteLine($"Matrix2x2ApplyContext configured: NumWords={ctx.NumWords}, det={ctx.U0 * ctx.V1 - ctx.V0 * ctx.U1}");
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
