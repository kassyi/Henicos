---
title: "BigIntKernelContext Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Context parameters for basic BigInt GPU kernels and MacroTranspiler expansions."
---

## BigIntKernelContext Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.Contexts`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Context parameters for basic BigInt GPU kernels and MacroTranspiler expansions.

```csharp
public struct BigIntKernelContext
```

## Examples

### Creating a BigInt Kernel Context

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.Contexts;

// Create and initialize a context for a basic BigInt kernel
var context = new BigIntKernelContext
{
    NumberOfWords = 4,
    Operands = new BigIntKernelContext.OperandsContext
    {
        LeftOperand = new uint[4] { 1, 2, 3, 4 },
        RightOperand = new uint[4] { 5, 6, 7, 8 },
        ResultBuffer = new uint[4],
        CarryBuffer = new long[4]
    }
};

// Outputs: Kernel Context created for 4 words.
Console.WriteLine($"Kernel Context created for {context.NumberOfWords} words.");
// Outputs: LeftOperand Length: 4
Console.WriteLine($"LeftOperand Length: {context.Operands.LeftOperand.Length}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuContext](../../../../03_bcl_api/math_bignum/gpu_context/GpuContext.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../../../02_architecture/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInteger with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
