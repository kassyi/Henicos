---
title: "GpuBigIntRef Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A ref struct representing a pointer to a BigInteger limb array in GPU device/shared memory during G# kernel transpilation."
---

## GpuBigIntRef Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

A `ref struct` representing a pointer to a BigInteger limb array in GPU device/shared memory during G# kernel transpilation.

```csharp
public readonly ref struct GpuBigIntRef
```

## Constructors

| Name                   | Description                                                          |
| ---------------------- | -------------------------------------------------------------------- |
| `GpuBigIntRef(uint[])` | Initializes a new reference struct wrapping the raw limb array data. |

## Properties / Fields

| Name   | Description                                     |
| ------ | ----------------------------------------------- |
| `Data` | Gets the raw underlying uint limb array buffer. |

## Operators

| Name                                     | Description                                                        |
| ---------------------------------------- | ------------------------------------------------------------------ |
| `operator +(GpuBigIntRef, GpuBigIntRef)` | Transpiles to `Device_Add` on the GPU.                             |
| `operator -(GpuBigIntRef, GpuBigIntRef)` | Transpiles to `Device_Sub` on the GPU.                             |
| `operator <<(GpuBigIntRef, int)`         | Transpiles to `Device_ShiftLeft` on the GPU.                       |
| `operator >>(GpuBigIntRef, int)`         | Transpiles to `Device_ShiftRight` on the GPU.                      |
| `operator &(GpuBigIntRef, GpuBigIntRef)` | Transpiles to `Device_BitwiseAnd` on the GPU.                      |
| `operator                                | (GpuBigIntRef, GpuBigIntRef)`                                      | Transpiles to `Device_BitwiseOr` on the GPU. |
| `operator ^(GpuBigIntRef, GpuBigIntRef)` | Transpiles to `Device_BitwiseXor` on the GPU.                      |
| `operator ~(GpuBigIntRef)`               | Transpiles to `Device_BitwiseNot` on the GPU.                      |
| `implicit operator GpuBigIntRef(uint[])` | Implicitly casts a raw `uint[]` to a `GpuBigIntRef`.               |
| `implicit operator uint[](GpuBigIntRef)` | Implicitly casts a `GpuBigIntRef` back to its underlying `uint[]`. |

## Examples

### CPU Simulation (Host)

> [!NOTE]
> `GpuBigIntRef` cannot be executed directly on the CPU. This example demonstrates the equivalent host-side simulation using `GpuBigIntMath`.

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt.Expressions;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);
using var uintA = GpuBigUInt.FromBigInteger(workspace, new System.Numerics.BigInteger(150), 4);
using var uintB = GpuBigUInt.FromBigInteger(workspace, new System.Numerics.BigInteger(50), 4);

// GpuBigIntRef operators throw on CPU. We use AST expression evaluation to simulate the result.
var sumExpr = uintA + uintB;
using var sumResult = (GpuBigUInt)sumExpr.Node.Evaluate(workspace, null);

Console.WriteLine($"Conceptually computes on GPU: 150 + 50 = {sumResult.ToBigInteger()}");
```

### Immediate Evaluation (GPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigInt;

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void KernelEntry()
    {
        uint[] bufA = new uint[4] { 150, 0, 0, 0 };
        uint[] bufB = new uint[4] { 50, 0, 0, 0 };

        // Cast to GpuBigIntRef
        GpuBigIntRef a = bufA;
        GpuBigIntRef b = bufB;

        // Operators are intercepted and transpiled by Roslyn to GpuBigIntKernels functions
        GpuBigIntRef sum = a + b;
    }
}
```

## Remarks

Overloads C# arithmetic operators to permit expressions like `a = b + c` inside GPU kernels. The GSharp Transpiler replaces these with optimized device function calls.

**Target:** GPU device functions and kernels only. Will throw `GpuOnlyException` if executed on the host.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
