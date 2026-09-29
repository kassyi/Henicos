---
title: "FermatContext Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Context parameters for the Fermat sequence computation."
---

## FermatContext Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.Contexts`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

Context parameters for the Fermat sequence computation.

```csharp
public struct FermatContext
```

## Examples

### Creating a Fermat Context

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using Kassyi.GSharp.Stdlib.Math.Contexts;

// Initialize dimensions for a Fermat transform context
var dimensions = new FermatContext.DimensionsContext
{
    NumberOfWords = 8,
    NumberOfWordsExtended = 16,
    Exponent = 3,
    BitOffset2E1 = 1
};

var context = new FermatContext
{
    Dimensions = dimensions
};

// Outputs: Fermat Context configured for 8 words.
Console.WriteLine($"Fermat Context configured for {context.Dimensions.NumberOfWords} words.");
// Outputs: Extended words: 16
Console.WriteLine($"Extended words: {context.Dimensions.NumberOfWordsExtended}");
```

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../../../02_architecture/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInteger with Custom Algorithms](../../../../04_guides/08_advanced_custom_math_kernels.md)
