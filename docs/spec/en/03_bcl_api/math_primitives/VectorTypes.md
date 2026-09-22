---
title: "Vector2, Vector3, Vector4 Structs (System.Numerics)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Information about how G# supports System.Numerics vector types natively."
---

## Vector2, Vector3, Vector4 Structs

## Definition

**Namespace:** `System.Numerics`  
**Assembly:** `System.Numerics.Vectors.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this type usage acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic type. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.

G# transparently maps standard .NET `System.Numerics.Vector2`, `Vector3`, and `Vector4` to CUDA's native `float2`, `float3`, and `float4` respectively.

## Remarks

Since these types map directly to CUDA native built-in vector types, they are optimally aligned in GPU memory. This often allows a single vectorized memory transaction (like `LD.E.128` for `float4`) to load all components simultaneously, drastically improving memory bandwidth utilization compared to sequential scalar loads.

## Examples

### Vector Computation

```csharp
// Execution Context: G# Kernel (GPU)
[GpuKernel]
public static unsafe void VectorCompute(Vector3* vectors)
{
    // Seamlessly compiled to CUDA's float3 and make_float3
    Vector3 vec = vectors[ThreadIndex.X];
    vec.Y += 10.0f;
    vectors[ThreadIndex.X] = new Vector3(vec.X, vec.Y, vec.Z);
}
```

## See also

- [Math / MathF Methods](./00_index.md)
- [Gpu BCL API Overview](../00_overview.md)
