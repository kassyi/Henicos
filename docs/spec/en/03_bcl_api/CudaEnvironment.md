---
title: "CudaEnvironment Class (G#)"
site: "G# Documentation"
published: "2026-09-23T00:00:00.000Z"
language: "en"
description: "Provides utility methods for discovering and resolving local NVIDIA CUDA Toolkit installation directories."
---

## CudaEnvironment Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Provides utility methods for discovering and resolving local NVIDIA CUDA Toolkit installation directories.

```csharp
public static class CudaEnvironment
```

## Properties

| Name | Description |
| --- | --- |
| [Resolver](#resolver) | Gets or sets the active CUDA path resolver strategy. Defaults to `DefaultCudaPathResolver`. |

## Resolver

Gets or sets the active CUDA path resolver strategy. Defaults to `DefaultCudaPathResolver`.

```csharp
public static ICudaPathResolver Resolver { get; set; }
```

## Methods

| Name | Description |
| --- | --- |
| [GetCudaPath()](#getcudapath) | Discovers and returns the root installation directory of the NVIDIA CUDA Toolkit. |
| [GetCudaBinPath()](#getcudabinpath) | Discovers and returns the path to the `bin` directory of the CUDA Toolkit. |

## GetCudaPath()

Discovers and returns the root installation directory of the NVIDIA CUDA Toolkit.

```csharp
public static string? GetCudaPath();
```

#### Returns

_string?_<br>
The absolute path to the CUDA installation directory if located; otherwise, `null`.

#### Exceptions

None. Returns `null` if the installation directory could not be discovered or does not exist.

## GetCudaBinPath()

Discovers and returns the path to the `bin` directory of the CUDA Toolkit containing binaries and dynamic libraries.

```csharp
public static string? GetCudaBinPath();
```

#### Returns

_string?_<br>
The absolute path to the CUDA `bin` directory (or `bin/x64` on Windows if present); otherwise, `null`.

#### Exceptions

None. Returns `null` if the CUDA Toolkit or its `bin` directory could not be resolved.

## Examples

### Resolving CUDA Paths

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
#nullable enable
using System;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU)
string? cudaPath = CudaEnvironment.GetCudaPath();
if (cudaPath != null)
{
    Console.WriteLine($"CUDA Toolkit is installed at: {cudaPath}");
    Console.WriteLine($"CUDA Binaries are located at: {CudaEnvironment.GetCudaBinPath()}");
}
else
{
    Console.WriteLine("CUDA Toolkit not found on this system.");
}
```

## Remarks

This utility queries system environment variables and default OS filesystem paths across Windows and Linux.
It searches the `CUDA_PATH` environment variable first, then inspects standard OS directories (e.g. `C:\Program Files\NVIDIA GPU Computing Toolkit\CUDA` on Windows or `/usr/local/cuda` on Linux).

### Customizing Path Discovery (`ICudaPathResolver` and `DefaultCudaPathResolver`)

Path discovery is delegated to the `ICudaPathResolver` strategy configured on `CudaEnvironment.Resolver`:

```csharp
public interface ICudaPathResolver
{
    string? GetCudaPath();
    string? GetCudaBinPath();
}

public class DefaultCudaPathResolver : ICudaPathResolver
{
    protected virtual string? GetEnvironmentVariable(string name);
    protected virtual bool DirectoryExists(string path);
    protected virtual string[] GetDirectories(string path, string searchPattern);
    protected virtual bool IsWindows();
    protected virtual bool IsLinux();
    public virtual string? GetCudaPath();
    public virtual string? GetCudaBinPath();
}
```

For unit testing or custom deployment environments, you can either implement `ICudaPathResolver` or subclass `DefaultCudaPathResolver` and override its protected virtual I/O methods.

## See also

### BCL API

- [BCL API and Hardware Intrinsics Reference](00_index.md)
