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

An assembly-level attribute used by the transpiler to map specific BCL method signatures directly to GPU hardware intrinsics without runtime reflection.

```csharp
public BclIntrinsicMapAttribute(
    string bclTypeFullName,
    string methodName,
    string[] parameterTypeFullNames,
    string cudaIntrinsicName);
```

- **`BclTypeFullName`** (_string_): Gets the fully qualified name of the target BCL type (e.g., `System.Numerics.BitOperations`).
- **`MethodName`** (_string_): Gets the name of the method within the target BCL type.
- **`ParameterTypeFullNames`** (_IReadOnlyList\<string\>_): Gets the list of fully qualified parameter type names used to resolve the specific method overload.
- **`CastFirstArgumentTo`** (_string?_): Gets or sets an optional C++ type cast applied to the first argument before calling the intrinsic (e.g., `unsigned int`).
- **`CudaIntrinsicName`** (_string_): Gets the CUDA C++ or PTX intrinsic function name to emit (e.g., `__popc`).

### `[GpuOperatorMacro]`

Maps C# operator overloads (like `+` or `*`) to specific GPU device functions via `TargetType` and `TargetMethod`.

An assignment `c = a + b` becomes a call `TargetMethod(c, a, b)`, or `TargetMethod(ctx, c, a, b)` when the method takes a context of a value type first (the one parameter or local of that type in scope). When `TargetMethod` is overloaded, G# picks the overload the way C# overload resolution would for a call with the operator's types (the return type for the result, the parameter types for the operands), not by declaration order:

- A candidate is a static `[GpuDeviceFunction]` method of that name with one parameter per result and operand, optionally after the context.
- A value parameter accepts an implicit conversion (user-defined conversions included). A `ref`, `out`, `in` or `ref readonly` parameter accepts only its own type, and only the result may be `ref` or `out`.
- Among the candidates that apply, G# chooses the one that is a better function member than every other, as C# does: no operand converts better to the other candidate, and at least one converts better to it. A conversion is better when the operand's type is the parameter type and the other parameter type is not; otherwise when its parameter type is the better conversion target (it converts implicitly to the other parameter type and not back, or it is a signed integral type and the other an unsigned one). A built-in conversion is not better than a user-defined one in itself: `Shl(uint[], uint[], long)` and `Shl(uint[], uint[], ShiftCount)` for an `int` shift are ambiguous, as they are in C#. When the parameter types are the same, a value parameter is better than an `in` or `ref readonly` one.
- No applicable overload, two equally good ones (for example the same parameters with and without a context), or an assignment target that does not convert to the chosen result parameter is reported as GSH0003, with the reason.

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
- [Advanced: Extending GpuBigInt with Custom Algorithms](../../04_guides/08_advanced_custom_math_kernels.md)
