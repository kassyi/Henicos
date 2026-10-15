---
title: "IGpuResourceScope Interface (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Defines a composite scope that tracks and collectively manages the lifetime of GPU resources."
---

## IGpuResourceScope Interface

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Defines a composite scope that tracks and collectively manages the lifetime of GPU resources.

```csharp
public interface IGpuResourceScope : IDisposable
```

## Methods

| Name | Description |
| --- | --- |
| [Track(IDisposable)](#trackidisposable) | Registers a disposable resource to be tracked by this scope. |
| [CreateScope()](#createscope) | Creates a nested child scope that is managed by the current scope. |

## Track(IDisposable)

Registers a disposable resource to be tracked by this scope. When the scope is disposed, all tracked resources will be disposed collectively.

```csharp
public void Track(IDisposable resource);
```

#### Parameters

`resource` _IDisposable_<br>
The disposable resource to track.

## CreateScope()

Creates a nested child scope that is managed by the current scope.

```csharp
public IGpuResourceScope CreateScope();
```

#### Returns

_IGpuResourceScope_<br>
A new `IGpuResourceScope`.

## Examples

### Collective GPU Resource Lifetime Management

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Collections.Generic;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU)

using IGpuResourceScope scope = new SimpleResourceScope();
scope.Track(new DummyResource("BufferA"));
using (IGpuResourceScope child = scope.CreateScope())
{
    child.Track(new DummyResource("BufferB"));
}

public sealed class DummyResource(string name) : IDisposable
{
    public void Dispose() => Console.WriteLine($"Disposed {name}");
}

public sealed class SimpleResourceScope : IGpuResourceScope
{
    private readonly List<IDisposable> _resources = new();
    public void Track(IDisposable resource) => _resources.Add(resource);
    public IGpuResourceScope CreateScope()
    {
        var child = new SimpleResourceScope();
        _resources.Add(child);
        return child;
    }
    public void Dispose()
    {
        for (int i = _resources.Count - 1; i >= 0; i--)
        {
            _resources[i].Dispose();
        }
        _resources.Clear();
    }
}
```

## Remarks

`IGpuResourceScope` provides hierarchical bulk-disposal semantics for GPU allocations (such as [`GpuWorkspace`](../math_bignum/gpu_workspace/GpuWorkspace.md)). Resources registered via `Track` or created within nested scopes via `CreateScope` are guaranteed to be released when the enclosing scope is disposed.

## See also

### BCL API > Memory

- [Memory Management](00_index.md)
- [SharedMemory<T> Struct (G#)](SharedMemory.md)

### BCL API > Attributes

- [GpuResourceAttribute Class (G#)](../attributes/GpuResourceAttribute.md)
- [TakesOwnershipAttribute Class (G#)](../attributes/TakesOwnershipAttribute.md)
