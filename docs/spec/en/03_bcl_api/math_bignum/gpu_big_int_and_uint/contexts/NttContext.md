---
title: "NttContext Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Context parameters for NTT-related operations in Fermat sequences."
---

## NttContext Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.Contexts`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Context parameters for NTT-related operations in Fermat sequences.

```csharp
public struct NttContext
```

## Examples

### Creating an NTT Context

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.Contexts;

// Configure an NTT Context
var context = new NttContext
{
    TransformLength = 1024,
    Buffers = new NttContext.BuffersContext
    {
        Buffer1 = new uint[1024],
        Buffer2 = new uint[1024],
        Buffer3 = new uint[1024],
        TemporaryBuffer = new uint[1024]
    }
};

// Outputs: NTT Context Transform Length: 1024
Console.WriteLine($"NTT Context Transform Length: {context.TransformLength}");
// Outputs: Buffer1 Size: 1024
Console.WriteLine($"Buffer1 Size: {context.Buffers.Buffer1.Length}");
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
