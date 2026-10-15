---
title: "CudaImportAttribute Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Indicates that a method imports an external CUDA kernel or device function defined in a separate CUDA module."
---

## CudaImportAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Indicates that a method imports an external CUDA kernel or device function defined in a separate CUDA module.

```csharp
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class CudaImportAttribute : Attribute
```

## Constructors

| Name | Description |
| --- | --- |
| [CudaImportAttribute(string)](#cudaimportattributestring) | Initializes a new instance of the `CudaImportAttribute` class with the specified module name. |

## CudaImportAttribute(string)

Initializes a new instance of the `CudaImportAttribute` class with the specified module name.

```csharp
public CudaImportAttribute(string moduleName);
```

#### Parameters

`moduleName` _string_<br>
The name or path of the external CUDA module file.

## Properties

| Name | Description |
| --- | --- |
| [ModuleName](#modulename) | Gets the name of the CUDA module containing the external kernel implementation. |
| [EntryPoint](#entrypoint) | Gets or sets the entry point name within the CUDA module. Defaults to the C# method name if empty. |
| [PackContext](#packcontext) | Gets or sets a value indicating whether kernel arguments should be packed into a context structure. |

## ModuleName

Gets the name of the CUDA module containing the external kernel implementation.

```csharp
public string ModuleName { get; }
```

## EntryPoint

Gets or sets the entry point name within the CUDA module. Defaults to the C# method name if empty.

```csharp
public string EntryPoint { get; set; }
```

## PackContext

Gets or sets a value indicating whether kernel arguments should be packed into a context structure.

```csharp
public bool PackContext { get; set; }
```

## Examples

### Importing an External CUDA Kernel

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Transpiler Inspection

Example.Run();

public static partial class Example
{
    [CudaImport("custom_kernels.ptx", EntryPoint = "custom_add_kernel", PackContext = true)]
    public static partial void CustomAdd(float[] a, float[] b, float[] c, int n);

    public static partial void CustomAdd(float[] a, float[] b, float[] c, int n)
    {
        // Generated or stub implementation for inspection
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(CustomAdd));
        var attr = method?.GetCustomAttribute<CudaImportAttribute>();

        Console.WriteLine($"ModuleName: {attr?.ModuleName}");
        Console.WriteLine($"EntryPoint: {attr?.EntryPoint}");
        Console.WriteLine($"PackContext: {attr?.PackContext}");
    }
}
```

## Remarks

Applied to C# `partial` or `extern` method declarations that are implemented in external CUDA source, PTX, or Fatbin files. When `EntryPoint` is omitted or empty, the source generator resolves the target symbol using the C# method name.

## See also

### BCL API > Attributes

- [Attributes](00_index.md)
- [GpuKernelAttribute Class (G#)](GpuKernelAttribute.md)
- [GpuDeviceFunctionAttribute Class (G#)](GpuDeviceFunctionAttribute.md)
