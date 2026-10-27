---
title: "GpuDeviceFunctionAttribute Class (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Marks a C# method to be transpiled into a GPU-callable device function."
---

## GpuDeviceFunctionAttribute Class

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Marks a C# method to be transpiled into a GPU-callable device function.

```csharp
[AttributeUsage(AttributeTargets.Method)]
public class GpuDeviceFunctionAttribute : Attribute

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuInlineAttribute : Attribute

[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public class GpuNoInlineAttribute : Attribute
```

## Examples

### Usage

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Transpiler Inspection

Example.Run();

public class Example
{
    [GpuDeviceFunction]
    [GpuInline]
    public static float ComputeDelta(float a, float b)
    {
        return MathF.Abs(a - b);
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(ComputeDelta));
        var hasAttr = method.GetCustomAttribute<GpuDeviceFunctionAttribute>() != null;

        Console.WriteLine($"Method '{method.Name}' has [GpuDeviceFunction]: {hasAttr}");
    }
}
```

## Remarks

Methods marked with `[GpuDeviceFunction]` are mapped to `__device__` functions in CUDA C++. They can only be called by `[GpuKernel]` methods or other `[GpuDeviceFunction]` methods.

### Calling a Device Function of Another Assembly

A kernel can call a `[GpuDeviceFunction]` declared in a referenced assembly, for example one of `Kassyi.GSharp.Stdlib.GpuMath`:

```csharp
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

public static partial class MyKernels
{
    [GpuKernel]
    public static void MontgomeryMuKernel(uint[] moduli, uint[] result)
    {
        int tid = ThreadIndex.X;
        result[tid] = CooperativeMath.ComputeMontgomeryMu(moduli[tid]);
    }
}
```

- The referenced assembly must be built with G# and export its device code: the default, unless it sets the MSBuild property `GSharpExportDeviceCode` to `false`. Its build records the transpiled text of each device function and struct (`[assembly: GpuDeviceExport]`), and the consumer copies the records that its kernels reach into its own native module, so the call is inlined as within one assembly ([1.7 Device Code Across Assemblies](../../02_architecture/01_aot_transpiler.md)).
- Functions that wait at a barrier or shuffle within a warp work the same way, also on the managed fallback of the CPU dialect, which runs the state machine that the library emits. The library emits it only when the function's containing types are `partial` and the function names no type less visible than `internal`; otherwise the call is reported with GSH0089 on the CPU dialect.
- The structs of the library can be used with their constructors, computed properties and `Dispose`. A constructor of a generic struct is available for the constructed types that the library's own device code uses.
- A use that cannot be imported is reported at the use: [GSH0088](../diagnostics/GSH0088.md) (no record), [GSH0089](../diagnostics/GSH0089.md) (another G# version, or the library could not transpile it for the dialect), [GSH0090](../diagnostics/GSH0090.md) (two assemblies declare the same path). An iterator (`IEnumerable<T>` with `yield`) of another assembly is not supported yet (GSH0003).

### Inlining Control (`[GpuInline]` and `[GpuNoInline]`)

- **`[GpuInline]` (`GpuInlineAttribute`)**: Requests force-inlining (`__forceinline__`) of the annotated device function in the transpiled CUDA C++ code.
- **`[GpuNoInline]` (`GpuNoInlineAttribute`)**: Prevents inlining (`__noinline__`) of the annotated device function in the transpiled CUDA C++ code.

## See also

### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [4. Execution and Concurrency Model](../../01_language/04_execution_model.md)
- [5. Limitations and Diagnostics](../../01_language/05_limitations.md)

### BCL API > Math Primitives

- [Math and BitOperations](../math_primitives/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../04_guides/08_advanced_custom_math_kernels.md)
