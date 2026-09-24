---
title: "FastMath.Sqrt Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the square root using hardware fast-math."
---

## FastMath.Sqrt Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the square root using hardware fast-math.

Returns the square root using hardware fast-math.

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using Kassyi.GSharp.Core;

try {
    // Sqrt uses fast hardware intrinsics on GPU
    float f = FastMath.Sqrt(16.0f);
    Console.WriteLine($"FastMath.Sqrt(16.0f) = {f}");
} catch (GpuOnlyException) {
    Console.WriteLine("FastMath functions can only be executed on the GPU.");
}
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed fast-math value.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using Kassyi.GSharp.Core;

try {
    // Sqrt uses fast hardware intrinsics on GPU
    float f = FastMath.Sqrt(16.0f);
    Console.WriteLine($"FastMath.Sqrt(16.0f) = {f:F1}");
} catch (GpuOnlyException) {
    Console.WriteLine("FastMath functions can only be executed on the GPU.");
}
```
## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Sqrt Method](Math_Sqrt.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)

