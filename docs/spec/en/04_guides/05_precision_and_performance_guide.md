---
title: "5. Precision and Performance Guide"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "A detailed breakdown of precision vs. performance on GPU architectures, focusing on single vs. double precision floats."
---

# 5. Precision and Performance Guide

When writing computational kernels for NVIDIA GPUs using G#, understanding the architectural differences between single-precision (`float`) and double-precision (`double`) floating-point operations is critical to achieving maximum performance.

---

## 5.1 Single vs. Double Precision on GPU Architectures

While standard C# and the .NET Base Class Library (BCL) heavily default to `double` (64-bit IEEE 754) for most `System.Math` functions, GPU hardware is fundamentally optimized for `float` (32-bit single precision).

### The Performance Penalty of `double`
Consumer GPUs (such as the GeForce RTX series) often have drastically reduced FP64 (double-precision) compute capabilities compared to FP32. The ratio of FP32 to FP64 cores can be as extreme as 1:64. 

This means that a kernel unintentionally using `double` arithmetic might run **up to 64x slower** than the equivalent `float` kernel, solely due to hardware execution unit availability.

### Memory Bandwidth Constraints
Beyond raw compute limitations, `double` values consume twice the VRAM and memory bandwidth of `float` values. Because GPU performance is frequently memory-bandwidth bound rather than compute-bound, cutting data sizes in half by using `float` allows twice as many values to be loaded from global memory per cycle, and doubles the effective capacity of the L1/L2 caches and Shared Memory.

---

## 5.2 `System.Math` vs `System.MathF`

Because C#'s `System.Math` methods typically return `double`, G# developers must exercise extreme caution.

**Avoid this pattern:**
```csharp
// Execution Context: G# Kernel (GPU)
float x = 1.5f;
// Math.Sin takes a double and returns a double. 
// 'x' is implicitly promoted to double, computed slowly on FP64 units, 
// and then cast back to float.
float result = (float)Math.Sin(x); 
```

**Use this pattern:**
```csharp
// Execution Context: G# Kernel (GPU)
float x = 1.5f;
// MathF.Sin operates purely on FP32 units at maximum speed.
float result = MathF.Sin(x); 
```

Whenever possible, explicitly use `System.MathF` equivalents for trigonometric, exponential, and logarithmic functions to ensure you remain on the fast FP32 execution path.

---

## 5.3 FastMath Approximations

If you need even higher throughput and can tolerate slight losses in precision, G# provides the `Kassyi.GSharp.Core.FastMath` namespace.

These methods (`FastMath.Exp`, `FastMath.Log`, `FastMath.Sqrt`) map directly to specialized hardware-accelerated approximation instructions (`__expf`, `__logf`, etc.) that execute in fewer clock cycles than the fully IEEE-754 compliant `MathF` equivalents.

## See also

- [Gpu BCL API Overview](../03_bcl_api/00_overview.md)
- [Standard Math Primitives Index](../03_bcl_api/math_primitives/00_index.md)
- [Warp-Level Programming Guide](01_warp_programming_guide.md)
- [Antipatterns and Best Practices Guide](06_antipatterns_and_best_practices.md)
