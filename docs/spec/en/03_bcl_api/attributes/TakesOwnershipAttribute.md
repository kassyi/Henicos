---
title: "Kassyi.GSharp.Core: TakesOwnershipAttribute class"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Marks a dependency-injected GPU resource as being strictly owned by the receiving class."
---

The **`TakesOwnershipAttribute`** marks a dependency-injected GPU resource as being owned by the receiving class. It interacts directly with the Zero-Leak Analyzer to ensure compile-time memory safety.

## Syntax

```csharp
public ClassConstructor([TakesOwnership] GpuContext context)
```

## Remarks

- **Borrowing (Default)**: By default, injecting a disposable GPU resource (like `GpuContext` or `GpuBuffer<T>`) into a constructor implies **Borrowing**. The analyzer (e.g., `GSH0020`, `GSH0021`) enforces that borrowed resources must *not* be disposed by the receiving class to prevent double-free errors.
- **Ownership Transfer**: Applying `[TakesOwnership]` explicitly transfers ownership to the receiving class. The analyzer then strictly enforces that the receiving class must implement `IDisposable` and must properly dispose of the resource in its `Dispose` method.

## Examples

### Explicit Resource Ownership

```csharp
// Execution Context: Host (CPU)
public class ComputePipeline : IDisposable
{
    private readonly GpuContext _context;

    // Explicitly takes ownership of the injected context
    public ComputePipeline([TakesOwnership] GpuContext context)
    {
        _context = context;
    }

    public void Dispose()
    {
        // Enforced by the Zero-Leak Analyzer
        _context.Dispose();
    }
}
```

## See also

### Related Attributes
- [Attributes Index](00_index.md)
- [ParameterInteropAttributes](ParameterInteropAttributes.md)

### Conceptual & Architecture
- [2. The Mental Model & Safety Net (Strict Ownership)](../../00_getting_started/02_mental_model.md#the-g-philosophy-strict-ownership-model)
- [2. Runtime Kernel Loader](../../02_architecture/02_runtime_loader.md)

### BCL & Resource Management
- [Gpu BCL API Overview](../00_index.md)
- [GpuContext](../math_bignum/gpu_context/GpuContext.md)

### Guides
- [Memory Safety Patterns Guide](../../04_guides/02_memory_safety_patterns.md)
- [Antipatterns and Best Practices Guide](../../04_guides/06_antipatterns_and_best_practices.md)
