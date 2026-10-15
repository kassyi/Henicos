---
title: "NativeLibraryProbe Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Probes native library candidates on behalf of generated DllImportResolver bindings."
---

## NativeLibraryProbe Class

## Definition

**Namespace:** `Kassyi.GSharp.Runtime.Interop`  
**Assembly:** `Kassyi.GSharp.Runtime.dll`

Probes native library candidates on behalf of the `DllImportResolver` emitted for YAML native API definitions that declare `libraryNames` or a path-qualified `libraryName`.

```csharp
public static class NativeLibraryProbe
```

## Methods

| Name | Description |
| --- | --- |
| [Load(Assembly, DllImportSearchPath?, ReadOnlySpan\<String\>)](#loadassembly-dllimportsearchpath-readonlyspanstring) | Loads the first candidate that resolves to a loadable library. |
| [ResolveCandidatePath(String, String)](#resolvecandidatepathstring-string) | Expands `%VARIABLE%` references and anchors relative paths to `baseDirectory`. |

## Load(Assembly, DllImportSearchPath?, ReadOnlySpan\<String\>)

Loads the first candidate that resolves to a loadable library.

```csharp
public static IntPtr Load(
    Assembly assembly,
    DllImportSearchPath? searchPath,
    ReadOnlySpan<string> candidates);
```

#### Parameters

`assembly` _Assembly_<br>
The assembly issuing the P/Invoke.

`searchPath` _DllImportSearchPath?_<br>
The search path requested by the P/Invoke, if any.

`candidates` _ReadOnlySpan\<String\>_<br>
Candidates in priority order; see `ResolveCandidatePath` for their syntax.

#### Returns

_IntPtr_<br>
The library handle, or `IntPtr.Zero` to fall back to the default runtime probing.

## ResolveCandidatePath(String, String)

Expands `%VARIABLE%` references and anchors relative paths to `baseDirectory`.

```csharp
public static string ResolveCandidatePath(string candidate, string baseDirectory);
```

#### Parameters

`candidate` _String_<br>
A bare file name (left to the OS search), a relative path such as `./native/lib.dll`, or an absolute path.

`baseDirectory` _String_<br>
The directory relative paths are resolved against, normally `AppContext.BaseDirectory`.

#### Returns

_String_<br>
The resolved path to pass to `NativeLibrary`.

## Examples

### Resolving a Native Library Candidate Path

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Runtime

```csharp
using System;
using System.IO;
using Kassyi.GSharp.Runtime.Interop;

// Execution Context: Host (CPU)
string resolved = NativeLibraryProbe.ResolveCandidatePath("./native/kassyi_cpu.dll", AppContext.BaseDirectory);
Console.WriteLine($"Resolved is rooted: {Path.IsPathRooted(resolved)}");
```

## See also

### BCL API > Host Runtime

- [Host Runtime](00_index.md)
- [CudaDriverApi and Driver Interop Types](CudaDriverApi.md)
