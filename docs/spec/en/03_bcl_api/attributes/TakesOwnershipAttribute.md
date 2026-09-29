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
public ClassConstructor([TakesOwnership] GpuWorkspace workspace)
```

## Remarks

- **Borrowing (Default)**: By default, injecting a disposable GPU resource (like `GpuWorkspace` or `GpuBuffer<T>`) into a constructor implies **Borrowing**. The analyzer (e.g., `GSH0020`, `GSH0021`) enforces that borrowed resources must *not* be disposed by the receiving class to prevent double-free errors.
- **Ownership Transfer**: Applying `[TakesOwnership]` explicitly transfers ownership to the receiving class. The analyzer then strictly enforces that the receiving class must implement `IDisposable` and must properly dispose of the resource in its `Dispose` method.

## Examples

### Explicit Resource Ownership

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.Math

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.Math;

// Execution Context: Host (CPU) Transpiler Inspection

Example.Run();

public class ComputePipeline : IDisposable
{
    private readonly GpuWorkspace _context;

    // Explicitly takes ownership of the injected context
    public ComputePipeline([TakesOwnership] GpuWorkspace workspace)
    {
        _context = context;
    }

    public void Dispose()
    {
        // Enforced by the Zero-Leak Analyzer
        _context.Dispose();
    }
}

public class Example
{
    public static void Run()
    {
        var constructor = typeof(ComputePipeline).GetConstructors()[0];
        var param = constructor.GetParameters()[0];
        var takesOwnership = param.GetCustomAttribute<TakesOwnershipAttribute>() != null;

        Console.WriteLine($"Constructor parameter '{param.Name}' has [TakesOwnership]: {takesOwnership}");
    }
}
```

## See also



### BCL API > Attributes

- [Attributes](00_index.md)

### Getting Started

- [0. Getting Started with G# (GSharp)](../../00_getting_started/00_index.md)
- [2. The Mental Model & Safety Net](../../00_getting_started/02_mental_model.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../02_architecture/00_index.md)
- [2. Runtime Execution and AOT Loader Pipeline (`Kassyi.GSharp.Runtime`)](../../02_architecture/02_runtime_loader.md)

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../math_bignum/00_index.md)
- [GpuWorkspace Class (G#)](../math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../../04_guides/02_memory_safety_patterns.md)

