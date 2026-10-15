---
title: "GSharpArrayProxy Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "A managed proxy struct representing the memory layout of the native GSharpArray<T> struct in CUDA."
---

## GSharpArrayProxy Struct

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Interop`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

A managed proxy struct representing the 16-byte memory layout of the native `GSharpArray<T>` struct in CUDA. Used for marshalling span and array arguments into explicit-layout AOT context structs.

```csharp
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GSharpArrayProxy
```

## Fields

| Name | Description |
| --- | --- |
| [Data](#data) | The raw device pointer (`IntPtr`) at byte offset `0`. |
| [Length](#length) | The element count (`int`) at byte offset `8`. |

## Data

The raw device pointer (`IntPtr`) at byte offset `0`.

```csharp
[FieldOffset(0)]
public IntPtr Data;
```

## Length

The element count (`int`) at byte offset `8`.

```csharp
[FieldOffset(8)]
public int Length;
```

## Operators

| Name | Description |
| --- | --- |
| [Implicit(IntPtr to GSharpArrayProxy)](#implicitintptr-to-gsharparrayproxy) | Implicitly converts a raw device pointer to a proxy struct with unknown length (`0`). |

## Implicit(IntPtr to GSharpArrayProxy)

Implicitly converts a raw device pointer to a proxy struct with unknown length (`0`). Used for backward compatibility with manual AOT marshaling.

```csharp
public static implicit operator GSharpArrayProxy(IntPtr ptr);
```

#### Parameters

`ptr` _IntPtr_<br>
The raw device pointer.

#### Returns

_GSharpArrayProxy_<br>
A `GSharpArrayProxy` with `Data = ptr` and `Length = 0`.

## Examples

### Constructing a GSharpArrayProxy

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Interop;

// Execution Context: Host (CPU)
GSharpArrayProxy proxy = IntPtr.Zero;
proxy.Length = 1024;
Console.WriteLine($"Proxy Ptr: {proxy.Data}, Length: {proxy.Length}");
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [GpuBuffer\<T\> Class](GpuBuffer.md)
