---
title: "Vector Value Types (Float2/3/4, Int2/3/4, etc.)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Information about G#'s native vector types (Float2, Int4, etc.) that map directly to CUDA hardware intrinsic types."
---

## Vector Value Types

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

G# introduces sequentially laid-out vector value types (e.g., `Float2`, `Float3`, `Float4`, `Int2`, `UInt4`, `Double2`) that match CUDA native vector memory layouts.

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using Kassyi.GSharp.Core;

// Using Float3
var v1 = new Float3(1.0f, 2.0f, 3.0f);
var v2 = new Float3(2.0f, 0.0f, 1.5f);

// Manual vector addition (as operators are mapped via transpiler intrinsically or implemented manually)
var result = new Float3(v1.X + v2.X, v1.Y + v2.Y, v1.Z + v2.Z);
Console.WriteLine($"({v1.X}, {v1.Y}, {v1.Z}) + ({v2.X}, {v2.Y}, {v2.Z}) = ({result.X}, {result.Y}, {result.Z})");
```

## Remarks

Since these types map directly to CUDA native built-in vector types (e.g., `float4`, `int2`), they are optimally aligned in GPU memory. This allows a single vectorized memory transaction (like `LD.E.128` for `Float4`) to load or store all components simultaneously, drastically improving memory bandwidth utilization compared to sequential scalar loads.

- **Alignment**: 2-component and 4-component vectors (e.g., `Float2`, `Float4`) are aligned to their total size (e.g. 8 bytes and 16 bytes). 3-component vectors (e.g., `Float3`) are aligned to the size of their base type, meaning they do not benefit from 128-bit vectorized loads.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
// Execution Context: G# Kernel (GPU) or Host (CPU fallback)
using System;
using Kassyi.GSharp.Core;

// Using Float3
var v1 = new Float3(1.0f, 2.0f, 3.0f);
var v2 = new Float3(2.0f, 0.0f, 1.5f);

// Manual vector addition (as operators are mapped via transpiler intrinsically or implemented manually)
var result = new Float3(v1.X + v2.X, v1.Y + v2.Y, v1.Z + v2.Z);
Console.WriteLine($"({v1.X}, {v1.Y}, {v1.Z}) + ({v2.X}, {v2.Y}, {v2.Z}) = ({result.X}, {result.Y}, {result.Z})");
```
## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
