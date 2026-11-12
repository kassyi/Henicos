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
public static class CpuKernelLoader
```

To run the kernels on the managed path although the native library exists, select
[`KernelExecution.Managed`](../execution_context/GpuExecution.md) with `GpuExecution.Use`.

## Methods

| Name | Description |
| --- | --- |
| [ShouldUseManagedExecution(string)](#shouldusemanagedexecutionstring) | Determines whether the managed execution fallback should be used for a given native DLL path. |
| [LoadAotKernelPtr(string, string)](#loadaotkernelptrstring-string) | Loads an AOT-compiled native CPU library and returns a raw function pointer. |
| [LoadAotKernel\<TDelegate\>(string, string)](#loadaotkerneltdelegatestring-string) | Loads an AOT-compiled native CPU library and returns a strongly-typed delegate. |
| [Shutdown()](#shutdown) | Unloads all cached CPU modules and clears the in-memory delegate cache. |
| [RegisterErrorSites(string, ReadOnlySpan\<GSharpErrorSite\>)](#registererrorsitesstring-readonlyspangsharperrorsite) | Registers the error site table of a native CPU library. |
| [ThrowIfNativeError(string)](#throwifnativeerrorstring) | Throws the managed exception for the error that a native CPU library recorded, if any. |

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
`true` if the current flow selected [`KernelExecution.Managed`](../execution_context/GpuExecution.md) or `dllPath` does not exist on disk; otherwise, `false`.

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

<a id="registererrorsitesstring-readonlyspangsharperrorsite"></a>

## RegisterErrorSites(string, ReadOnlySpan\<GSharpErrorSite\>)

Registers the error site table of a native CPU library, so that an error the library records can be reported with its source location. The generated launchers call it; user code does not need to.

```csharp
public static void RegisterErrorSites(string dllPath, ReadOnlySpan<GSharpErrorSite> sites);
```

#### Parameters

`dllPath` _string_<br>
The file path to the native dynamic library.

`sites` _ReadOnlySpan\<GSharpErrorSite\>_<br>
The 1-based error site table.

## ThrowIfNativeError(string)

Reads the error slot of a native CPU library and, when a kernel recorded an error (an out-of-range index, a division by zero ...), throws the corresponding managed exception with the source location. The generated launchers call it after each launch; user code does not need to.

```csharp
public static void ThrowIfNativeError(string dllPath);
```

#### Parameters

`dllPath` _string_<br>
The file path to the native dynamic library.

## Examples

### Controlling Managed CPU Fallback Execution

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using Kassyi.GSharp.Runtime;
using Kassyi.GSharp.Runtime.Aot;

// Execution Context: Host (CPU)

string existing = typeof(CpuKernelLoader).Assembly.Location;
using (GpuExecution.Use(KernelExecution.Managed))
{
    bool useManaged = CpuKernelLoader.ShouldUseManagedExecution(existing);
    Console.WriteLine($"ShouldUseManagedExecution: {useManaged}");   // True: the managed path is selected
}
```

## Remarks

`CpuKernelLoader` validates the 64-byte trailing Ed25519 signature of native CPU kernel binaries before calling `NativeLibrary.Load`. Loaded delegates are cached in a thread-safe `ConcurrentDictionary` and can be released via `Shutdown()`.

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [GpuKernelLoader Class (G#)](GpuKernelLoader.md)
