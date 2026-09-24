---
title: "Kassyi.GSharp.Core: Advanced Mapping Attributes"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Advanced attributes used primarily by the standard library to map C# constructs to native CUDA C++ representations."
---

## Advanced Mapping Attributes

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

The `Kassyi.GSharp.Core` package includes several advanced internal mapping attributes. These are used primarily by the G# Base Class Library (BCL) and the standard library to map C# constructs to native CUDA C++ representations during transpilation.

```csharp
[AttributeUsage(AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field, Inherited = false)]
public sealed class CudaIntrinsicAttribute : Attribute

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class CudaIntrinsicPatternAttribute : Attribute

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class BclIntrinsicMapAttribute : Attribute

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
public sealed class GpuOperatorMacroAttribute : Attribute
```

## Attributes

### `[CudaIntrinsic]`

Maps a specific C# method directly to a CUDA C++ function call.

### `[CudaIntrinsicPattern(string pattern)]`

A Class/Struct-level attribute used to map all members of a type to a C++ pattern.
The `{0}` token represents the lowercase name of the invoked member.

- _Example_: `[CudaIntrinsicPattern("threadIdx.{0}")]` maps `ThreadIndex.X` to `threadIdx.x`.

### `[BclIntrinsicMap]`

An assembly-level attribute used by the transpiler to map specific BCL method signatures directly to GPU hardware intrinsics.

- _Example_: Maps `System.Numerics.BitOperations.PopCount` to `__popc`.

### `[GpuOperatorMacro]`

Maps C# operator overloads (like `+` or `*`) to specific GPU device functions via `TargetType` and `TargetMethod`.

## Examples

### Custom GPU Intrinsics

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU)

// Trigger top-level statements (must appear before type declarations)
KernelExample.Run();

[CudaIntrinsicPattern("threadIdx.{0}")]
public struct CustomThreadIndex
{
    // The G# transpiler replaces invocations of this property with "threadIdx.x".
    // In standard C# (for unit tests or CPU fallback), it returns a placeholder value.
    public static int X => 0;
}

public static class NativeFunctions
{
    // The G# transpiler replaces calls to this method with the CUDA C++ function "__popc".
    [CudaIntrinsic("__popc")]
    public static int PopCount(uint value) => 0;
}

// Example usage within a GPU kernel
public class KernelExample
{
    public static void Run()
    {
        int tx = CustomThreadIndex.X;
        int popc = NativeFunctions.PopCount(42);
        
        Console.WriteLine($"CustomThreadIndex.X = {tx}");
        Console.WriteLine($"NativeFunctions.PopCount(42) = {popc}");
    }
}
```

## See also

### BCL API

- [BCL API and Hardware Intrinsics Reference](../00_index.md)
- [10. Standard BCL Mappings](../02_bcl_mappings.md)

### BCL API > Gpu Class

- [Gpu Class](../gpu_class/00_index.md)
- [Gpu.InlinePtx Method (G#)](../gpu_class/InlinePtx.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [Advanced: Extending GpuBigInteger with Custom Algorithms](../../04_guides/08_advanced_custom_math_kernels.md)
