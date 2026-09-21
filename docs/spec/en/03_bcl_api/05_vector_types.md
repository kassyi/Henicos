# 5. Vector Types Reference

The `Kassyi.GSharp.Core` namespace provides a complete set of strongly typed vector structs that map directly to NVIDIA CUDA's native vector types (e.g., `float4`, `int2`, `uint3`).

These structures are fundamentally essential for maximizing memory bandwidth, as CUDA hardware is optimized to read and write 64-bit and 128-bit aligned contiguous memory chunks in a single transaction (vectorized memory access).

---

## 5.1 Supported Vector Types

All vector types are structs decorated with `[StructLayout(LayoutKind.Sequential)]` to guarantee strict memory layout equivalence with native CUDA C++.

| Base Type | 2-Component (64-bit) | 3-Component (96-bit) | 4-Component (128-bit) | CUDA Mapping |
| :--- | :--- | :--- | :--- | :--- |
| `float` | `Float2` | `Float3` | `Float4` | `float2`, `float3`, `float4` |
| `double` | `Double2` | `Double3` | `Double4` | `double2`, `double3`, `double4` |
| `int` | `Int2` | `Int3` | `Int4` | `int2`, `int3`, `int4` |
| `uint` | `UInt2` | `UInt3` | `UInt4` | `uint2`, `uint3`, `uint4` |
| `long` | `Long2` | `Long3` | `Long4` | `longlong2`, `longlong3`, `longlong4` |
| `ulong` | `ULong2` | `ULong3` | `ULong4` | `ulonglong2`, `ulonglong3`, `ulonglong4` |

---

## 5.2 Accessing Components

All vectors expose their fields as public, mutable fields: `X`, `Y`, `Z`, and `W` (where applicable based on vector length).

#### Syntax Example
```csharp
public struct Float4
{
    public float X;
    public float Y;
    public float Z;
    public float W;
    
    public Float4(float x, float y, float z, float w) { /* ... */ }
}
```

---

## 5.3 Vectorized Memory Access

Using built-in vector types is the standard way to achieve peak memory throughput in G# kernels. 
By loading a `Float4` instead of four separate `float` values sequentially, the GPU issues a single 128-bit LDG (Load Global) instruction rather than four separate 32-bit instructions.

### Example: Vectorized Copy

> [!TIP]
> Casting a `float[]` array to a `Float4*` pointer (via `fixed` or `ref` semantics) and copying it allows the memory controller to maximize bandwidth saturation.

```csharp
[GpuKernel]
public static unsafe void VectorizedArrayCopy(float* source, float* destination, int totalElements)
{
    int globalId = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
    
    // Each thread processes 4 floats (16 bytes) at once
    int totalVectors = totalElements / 4;
    
    if (globalId < totalVectors)
    {
        // Cast raw pointers to 128-bit Float4 pointers
        Float4* srcVec = (Float4*)source;
        Float4* dstVec = (Float4*)destination;

        // Compiles down to a single 128-bit memory load and store instruction
        Float4 data = srcVec[globalId];
        dstVec[globalId] = data;
    }
}
```

---

## 5.4 Memory Alignment Guarantees

NVIDIA hardware requires that 64-bit and 128-bit memory accesses be properly aligned. 

- `Float2`, `Int2`, `UInt2` (64-bit vectors): Must be aligned to **8 bytes**.
- `Float4`, `Int4`, `UInt4` (128-bit vectors): Must be aligned to **16 bytes**.

When G# transpiles these structs to CUDA C++, they automatically inherit the `__align__(8)` and `__align__(16)` attributes provided by the native CUDA headers.

> [!CAUTION]
> A 3-component vector (e.g., `Float3`) is **not** 16-byte aligned by default. It is internally packed as three separate 32-bit values (12 bytes). Therefore, a `Float3` cannot be read via a single 128-bit hardware instruction. For peak performance, padding data to `Float4` is usually recommended over using `Float3`.
