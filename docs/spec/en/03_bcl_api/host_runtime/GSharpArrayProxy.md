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

A managed proxy struct representing the 16-byte memory layout of the native `GSharpArray<T>` struct in CUDA. It is the
type of the array parameters of the `Launch{Kernel}Ptr` launchers and of the array fields of the native shadow structs
(`AotContexts`): the kernel sees `Data` as the array and `Length` as its `Length`.

```csharp
[StructLayout(LayoutKind.Explicit, Size = 16)]
public struct GSharpArrayProxy
```

A `null` array is `Data == IntPtr.Zero`. An empty array has a non-null `Data` that is never dereferenced
([EmptyArrayData](#emptyarraydata) on CUDA), so the device tells `null` from empty as C# does.

## Constructors

| Name | Description |
| --- | --- |
| [GSharpArrayProxy(IntPtr, int)](#gsharparrayproxyintptr-int) | Initializes a proxy for `length` elements at `data`. |

## GSharpArrayProxy(IntPtr, int)

Initializes a proxy for `length` elements at `data`, with no [Flags](#flags).

```csharp
public GSharpArrayProxy(IntPtr data, int length);
```

#### Parameters

`data` _IntPtr_<br>
The address of the first element, or `IntPtr.Zero` for a `null` array.

`length` _int_<br>
The number of elements the kernel sees as the array's `Length`.

#### Exceptions

[ArgumentOutOfRangeException](https://learn.microsoft.com/en-us/dotnet/api/system.argumentoutofrangeexception)<br>
`length` is negative.

[ArgumentException](https://learn.microsoft.com/en-us/dotnet/api/system.argumentexception)<br>
`data` is `IntPtr.Zero` and `length` is not `0`.

## Fields

| Name | Description |
| --- | --- |
| [Data](#data) | The raw device pointer (`IntPtr`) at byte offset `0`. |
| [Length](#length) | The element count (`int`) at byte offset `8`. |
| [Flags](#flags) | Bit flags (`int`) at byte offset `12`. |
| [EmptyArrayData](#emptyarraydata) | The `Data` of an empty array on CUDA. |
| [NullReadsAsEmpty](#nullreadsasempty) | The `Flags` bit of a `null` optional kernel argument. |

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

## Flags

Bit flags (`int`) at byte offset `12`, `GSharpArray<T>::flags` on the device. The launchers set
[NullReadsAsEmpty](#nullreadsasempty) for a `null` optional kernel argument; every other value has `0`.

```csharp
[FieldOffset(12)]
public int Flags;
```

## EmptyArrayData

The `Data` of an empty array on CUDA: a non-null address (`256`) that is never dereferenced, aligned for any element
type. No allocation returns it (`cuMemAlloc` rejects zero bytes).

```csharp
public const nint EmptyArrayData = 256;
```

## NullReadsAsEmpty

The [Flags](#flags) bit (`1`) that the launchers set for a `null` optional kernel argument (`T[]?`). The device reads such
an argument as an array of length 0, also through a local or a device function, while reading any other `null` array
throws `NullReferenceException`.

```csharp
public const int NullReadsAsEmpty = 1;
```

## Remarks

There is no conversion from `IntPtr`: a pointer alone has no length, and a proxy made from one would let the kernel see
`Length == 0`. Build a proxy with the [constructor](#gsharparrayproxyintptr-int), or with
[GpuBuffer\<T\>.ToProxy](GpuBuffer.md#toproxy) for a device buffer.

The `Launch{Kernel}Ptr` launchers check an array argument as the array launchers check an array: a non-optional
parameter rejects a `null` proxy (`ArgumentNullException`) and an empty one (`ArgumentException`), an optional one
(`T[]?`) takes an empty proxy as `null`, and a negative `Length` is rejected (`ArgumentOutOfRangeException`), all before
the launch. The launcher sets the [Flags](#flags) itself; the caller's value never reaches the device.

## Examples

### Constructing a GSharpArrayProxy

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Interop;

// Execution Context: Host (CPU)
// A null array, and 1024 elements at an address that the caller owns
GSharpArrayProxy none = default;
var proxy = new GSharpArrayProxy(new IntPtr(0x7000_0000), 1024);
Console.WriteLine($"Null: {none.Data}, Proxy Ptr: {proxy.Data}, Length: {proxy.Length}");
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [GpuBuffer\<T\> Class](GpuBuffer.md)
