---
title: "FastMath.Abs Method"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Returns the absolute value of a specified number using hardware fast-math."
---

## FastMath.Abs Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the absolute value of a specified number using hardware fast-math.

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using Kassyi.GSharp.Core;

try {
    // Abs uses fast hardware intrinsics on GPU
    float f = FastMath.Abs(-5.5f);
    Console.WriteLine($"FastMath.Abs(-5.5f) = {f}");
} catch (GpuOnlyException) {
    Console.WriteLine("FastMath functions can only be executed on the GPU.");
}
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed fast-math absolute value.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using Kassyi.GSharp.Core;

try {
    // Abs uses fast hardware intrinsics on GPU
    float f = FastMath.Abs(-5.5f);
    Console.WriteLine($"FastMath.Abs(-5.5f) = {f:F1}");
} catch (GpuOnlyException) {
    Console.WriteLine("FastMath functions can only be executed on the GPU.");
}
```
## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Abs Method](Math_Abs.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
