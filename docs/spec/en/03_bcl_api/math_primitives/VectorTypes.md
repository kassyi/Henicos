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

G# provides 18 sequentially laid-out vector value types that match the CUDA built-in vector types: `Float2`..`Float4`, `Int2`..`Int4`, `UInt2`..`UInt4`, `Double2`..`Double4`, `Long2`..`Long4` and `ULong2`..`ULong4` (CUDA `float2`..`float4`, `int2`..`int4`, `uint2`..`uint4`, `double2`..`double4`, `longlong2`..`longlong4`, `ulonglong2`..`ulonglong4`). `System.Numerics.Vector2`, `Vector3` and `Vector4` are also supported in kernels and map to `float2`, `float3` and `float4`. Their operators and methods are not mapped yet: an operator such as `a + b` is reported as [GSH0060](../diagnostics/GSH0060.md) and a method such as `Vector3.Dot` as [GSH0109](../diagnostics/GSH0109.md).

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

- **Alignment**: 2-component vectors are aligned to their total size (8 bytes for `Float2`, 16 bytes for `Double2`). 4-component vectors are aligned to their total size up to 16 bytes: `Float4` to 16 bytes, and the 32-byte `Double4`, `Long4` and `ULong4` also to 16 bytes, as the CUDA types are. 3-component vectors (e.g., `Float3`) are aligned to the size of their element type, so they do not benefit from vectorized loads.

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
