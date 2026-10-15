---
title: "GpuResourceAttribute Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Marks a class or struct as a GPU resource whose lifecycle must be managed via IGpuResourceScope."
---

## GpuResourceAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a class or struct as a GPU resource whose lifecycle must be managed via `IGpuResourceScope`. This attribute is used by static code analysis to enforce proper disposal.

```csharp
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
public sealed class GpuResourceAttribute : Attribute
```

## Examples

### Marking a Custom GPU Resource

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU)

var isGpuResource = typeof(CustomDeviceBuffer).GetCustomAttribute<GpuResourceAttribute>() != null;
Console.WriteLine($"CustomDeviceBuffer has [GpuResource]: {isGpuResource}");

[GpuResource]
public sealed class CustomDeviceBuffer : IDisposable
{
    public void Dispose()
    {
    }
}
```

## Remarks

Types annotated with `[GpuResource]` are tracked by the G# Zero-Leak Roslyn Analyzer to ensure that every allocated instance is either disposed explicitly (`using`), tracked by an [`IGpuResourceScope`](../memory/IGpuResourceScope.md), or transferred via [`[TakesOwnership]`](TakesOwnershipAttribute.md).

## See also

### BCL API > Attributes

- [Attributes](00_index.md)
- [TakesOwnershipAttribute Class (G#)](TakesOwnershipAttribute.md)

### BCL API > Memory

- [Memory Management](../memory/00_index.md)
- [IGpuResourceScope Interface (G#)](../memory/IGpuResourceScope.md)

### Guides

- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../../04_guides/02_memory_safety_patterns.md)
