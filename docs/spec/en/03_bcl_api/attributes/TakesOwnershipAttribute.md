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



### Getting Started

- [0. Getting Started with G# (GSharp)](../../00_getting_started/00_index.md)
- [2. The Mental Model & Safety Net](../../00_getting_started/02_mental_model.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../02_architecture/00_index.md)
- [2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)](../../02_architecture/02_runtime_loader.md)

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../math_bignum/00_index.md)
- [GpuContext Class (G#)](../math_bignum/gpu_context/GpuContext.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../../04_guides/02_memory_safety_patterns.md)

