---
title: "CpuKernelLoader Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Loads AOT-compiled native CPU kernel libraries, verifies digital signatures, and manages managed execution fallbacks."
---

## CpuKernelLoader Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Aot`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Loads AOT-compiled native CPU kernel libraries, manages function pointer/delegate caching, and controls fallback to pure C# managed execution.

```csharp
public sealed class CpuKernelLoader
```

## Properties

| Name | Description |
| --- | --- |
| [ForceManagedExecution](#forcemanagedexecution) | Gets or sets a value indicating whether to forcefully bypass native AOT DLL execution and use pure C# managed execution. |

## ForceManagedExecution

Gets or sets a value indicating whether to forcefully bypass native AOT DLL execution and use pure C# managed execution (stored per async flow in an `AsyncLocal<bool>`).

```csharp
public static bool ForceManagedExecution { get; set; }
```

## Methods

| Name | Description |
| --- | --- |
| [ShouldUseManagedExecution(string)](#shouldusemanagedexecutionstring) | Determines whether the managed execution fallback should be used for a given native DLL path. |
| [LoadAotKernelPtr(string, string)](#loadaotkernelptrstring-string) | Loads an AOT-compiled native CPU library and returns a raw function pointer. |
| [LoadAotKernel\<TDelegate\>(string, string)](#loadaotkerneltdelegatestring-string) | Loads an AOT-compiled native CPU library and returns a strongly-typed delegate. |
| [Shutdown()](#shutdown) | Unloads all cached CPU modules and clears the in-memory delegate cache. |

## ShouldUseManagedExecution(string)

Determines whether the managed execution fallback should be used.

```csharp
public static bool ShouldUseManagedExecution(string dllPath);
```

#### Parameters

`dllPath` _string_<br>
The path to the native DLL to check.

#### Returns

_bool_<br>
`true` if `ForceManagedExecution` is enabled or `dllPath` does not exist on disk; otherwise, `false`.

## LoadAotKernelPtr(string, string)

Loads an AOT-compiled native CPU library after verifying its embedded Ed25519 digital signature and returns a raw function pointer.

```csharp
public static IntPtr LoadAotKernelPtr(string kernelName, string dllPath);
```

#### Parameters

`kernelName` _string_<br>
The name of the exported kernel function.

`dllPath` _string_<br>
The file path to the native dynamic library.

#### Returns

_IntPtr_<br>
A native function pointer (`IntPtr`) to the exported kernel.

#### Exceptions

`InvalidOperationException`<br>
Thrown if the binary is too small or its embedded Ed25519 signature fails verification against trusted public keys.

## LoadAotKernel\<TDelegate\>(string, string)

Loads an AOT-compiled native CPU library after verifying its embedded Ed25519 digital signature and returns a strongly-typed delegate.

```csharp
public static TDelegate LoadAotKernel<TDelegate>(string kernelName, string dllPath) where TDelegate : Delegate;
```

#### Type Parameters

`TDelegate`<br>
The delegate type matching the native kernel signature.

#### Parameters

`kernelName` _string_<br>
The name of the exported kernel function.

`dllPath` _string_<br>
The file path to the native dynamic library.

#### Returns

_TDelegate_<br>
A strongly-typed delegate wrapping the native kernel function.

#### Exceptions

`InvalidOperationException`<br>
Thrown if the binary's embedded Ed25519 signature fails verification.

## Shutdown()

Unloads all cached CPU modules and clears the in-memory cache to reclaim memory.

```csharp
public static void Shutdown();
```

## Examples

### Controlling Managed CPU Fallback Execution

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime.Aot;

// Execution Context: Host (CPU)

CpuKernelLoader.ForceManagedExecution = true;
bool useManaged = CpuKernelLoader.ShouldUseManagedExecution("nonexistent_kernel.dll");
Console.WriteLine($"ShouldUseManagedExecution: {useManaged}");
CpuKernelLoader.ForceManagedExecution = false;
```

## Remarks

`CpuKernelLoader` validates the 64-byte trailing Ed25519 signature of native CPU kernel binaries before calling `NativeLibrary.Load`. Loaded delegates are cached in a thread-safe `ConcurrentDictionary` and can be released via `Shutdown()`.

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [GpuKernelLoader Class (G#)](GpuKernelLoader.md)
