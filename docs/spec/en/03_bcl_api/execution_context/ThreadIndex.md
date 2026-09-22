---
title: "ThreadIndex Struct (Kassyi.GSharp.Core)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Gets the thread index within the block."
---

## ThreadIndex Struct

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Gets the thread index within the block.

```csharp
public readonly struct ThreadIndex
```

## Properties

| Name | Description |
| --- | --- |
| [ThreadIndex.X](#x) | The X coordinate/index. |
| [ThreadIndex.Y](#y) | The Y coordinate/index. |
| [ThreadIndex.Z](#z) | The Z coordinate/index. |

## X

The X coordinate/index.

```csharp
public static int X { get; }
```

#### Property Value

`int`  
The X coordinate/index.

## Y

The Y coordinate/index.

```csharp
public static int Y { get; }
```

#### Property Value

`int`  
The Y coordinate/index.

## Z

The Z coordinate/index.

```csharp
public static int Z { get; }
```

#### Property Value

`int`  
The Z coordinate/index.

## Remarks

Maps directly to the `threadIdx` built-in variable in CUDA PTX.
These properties are strictly designed for execution within a `[GpuKernel]` or `[GpuDeviceFunction]` on the GPU. Accessing them from the Host (CPU) execution context will throw a `Kassyi.GSharp.Core.GpuOnlyException`.

## See also

- [Execution Context](./00_index.md)
- [Gpu BCL API Overview](../00_index.md)
