---
title: "GpuBigIntRef Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A ref struct representing a pointer to a BigInteger limb array in GPU device/shared memory during G# kernel transpilation."
---

## GpuBigIntRef Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

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

### Pointer Arithmetics in a GPU Kernel

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math.BigInt;

// Execution Context: Host (CPU)
Example.KernelEntry();

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void KernelEntry()
    {
        uint[] bufA = new uint[4];
        uint[] bufB = new uint[4];

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

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
