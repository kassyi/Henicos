---
title: "Gpu.Hint.Unroll Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Hints the compiler to unroll the immediately following loop."
---

## Gpu.Hint.Unroll Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Hints the compiler to unroll the immediately following loop.

## Overloads

| Name | Description |
| --- | --- |
| [Unroll(int)](#unroll-int) | Hints the compiler to unroll the immediately following loop by a specific factor (or completely if 0). |

## Unroll(int)

Hints the compiler to unroll the immediately following loop by a specific factor.

```csharp
[GpuKernel]
public static void Unroll(int factor = 0);
```

#### Parameters

`factor` <i>int</i><br>
The unroll factor. If `0`, the loop is completely unrolled.

## Examples

### Loop Unrolling

```csharp
// Execution Context: G# Kernel (GPU)
// Completely unroll the loop (assuming fixed bounds)
Gpu.Hint.Unroll();
for (int i = 0; i < 4; i++)
{
    // ...
}

// Unroll by a factor of 2
Gpu.Hint.Unroll(2);
for (int i = 0; i < count; i++)
{
    // ...
}
```

## Remarks

Maps to `#pragma unroll` or `#pragma unroll N`. Must be placed immediately before a `for` or `while` loop.

## See also

### Loop & Kernel Flow Control
- [GridStrideLoop](GridStrideLoop.md)
- [SingleThread](SingleThread.md)

### Language & Transpiler Architecture
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)
- [1. AOT Transpiler Architecture](../../02_architecture/01_aot_transpiler.md)
- [Gpu BCL API Overview](../00_index.md)

### Performance Guides
- [Precision and Performance Guide](../../04_guides/05_precision_and_performance_guide.md)
- [Antipatterns and Best Practices Guide](../../04_guides/06_antipatterns_and_best_practices.md)
