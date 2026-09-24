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
[StructLayout(LayoutKind.Sequential)]
public struct Float4
{
    public float X; public float Y; public float Z; public float W;
    public Float4(float x, float y, float z, float w);
}
// (Equivalent definitions exist for Int2/3/4, UInt2/3/4, Double2/3/4, Long2/3/4, ULong2/3/4)
```

## Remarks

Since these types map directly to CUDA native built-in vector types (e.g., `float4`, `int2`), they are optimally aligned in GPU memory. This allows a single vectorized memory transaction (like `LD.E.128` for `Float4`) to load or store all components simultaneously, drastically improving memory bandwidth utilization compared to sequential scalar loads.

- **Alignment**: 2-component and 4-component vectors (e.g., `Float2`, `Float4`) are aligned to their total size (e.g. 8 bytes and 16 bytes). 3-component vectors (e.g., `Float3`) are aligned to the size of their base type, meaning they do not benefit from 128-bit vectorized loads.

## Examples

### Vectorized Memory Access

> [!NOTE] Requires assemblies: Kassyi.GSharp.Core

```csharp

using Kassyi.GSharp.Core;

// Execution Context: Host (CPU)
Example.Main();

public class Example
{
    public static void Main() {}

    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static unsafe void VectorCompute(Float4* vectors)
    {
        // Seamlessly compiled to CUDA's float4 and make_float4
        // Uses a single 128-bit memory load (LD.E.128)
        Float4 vec = vectors[ThreadIndex.X];
        vec.Y += 10.0f;
        vectors[ThreadIndex.X] = new Float4(vec.X, vec.Y, vec.Z, vec.W);
    }
}
```

## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [5. Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
