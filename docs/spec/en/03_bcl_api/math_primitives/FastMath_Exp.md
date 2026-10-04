---
title: "FastMath.Exp Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns e raised to the specified power using hardware fast-math."
---

## FastMath.Exp Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns e raised to the specified power using hardware fast-math.

```csharp
[GpuKernel]
public static float Exp(float x);
```

#### Parameters

`x` <i>float</i><br>
A single-precision floating-point number.

#### Returns

<i>float</i><br>
The computed fast-math value.

#### Exceptions

[GpuOnlyException](../01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when evaluated on the host CPU.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using Kassyi.GSharp.Core;

try {
    // Exp uses fast hardware intrinsics on GPU
    float f = FastMath.Exp(1.0f);
    Console.WriteLine($"FastMath.Exp(1.0f) = {f:F1}");
} catch (GpuOnlyException) {
    Console.WriteLine("FastMath functions can only be executed on the GPU.");
}
```
## See also



### BCL API > Math Primitives

- [Math and BitOperations](00_index.md)
- [Math / MathF.Exp Method](Math_Exp.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)

