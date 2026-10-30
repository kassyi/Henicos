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

- A candidate is a static, non-generic `[GpuDeviceFunction]` method of that name with one parameter per result and operand, optionally after the context.
- A value parameter accepts an implicit conversion (user-defined conversions included). A `ref`, `out`, `in` or `ref readonly` parameter accepts only its own type, and only the result may be `ref` or `out`.
- Among the candidates that apply, G# chooses the one that is a better function member than every other, as C# does: no operand converts better to the other candidate, and at least one converts better to it. A conversion is better when the operand's type is the parameter type and the other parameter type is not; otherwise when its parameter type is the better conversion target (it converts implicitly to the other parameter type and not back, or it is a signed integral type and the other an unsigned one). A built-in conversion is not better than a user-defined one in itself: `Shl(uint[], uint[], long)` and `Shl(uint[], uint[], ShiftCount)` for an `int` shift are ambiguous, as they are in C#. When the parameter types are the same, a value parameter is better than an `in` or `ref readonly` one.
- No applicable overload, two equally good ones (for example the same parameters with and without a context), or an assignment target that does not convert to the chosen result parameter is reported as GSH0003, with the reason.

An operator macro is expanded only as the right side of an assignment statement (inside parentheses and casts, if any), or as an operand of an operator macro there. An operator macro used anywhere else (a statement of its own such as `++x;`, the initializer of a declaration, an argument) is reported as GSH0003.

An operator macro used as an operand of another one (`c = c - (a - b)`) is computed into a temporary first, in the C# order of evaluation, when its target takes the result as a `ref` or `out` parameter of an unmanaged type (`Combine(ref Value result, Value left, Value right)`). An operand before it that its call could change is evaluated into a temporary before the call. A target that writes the result into the storage of the assignment target (`Add(uint[] result, ...)`) has no temporary to compute into, and the nested use is reported as GSH0003.

A compound assignment whose operator or right side is an operator macro (`c += a + b` would otherwise compute `c = a + b`), an increment or decrement operator macro (`r = ++x`), and an operator macro applied to nullable operands (a lifted operator) are reported as GSH0003. Write `t = a + b; c = c + t;` instead of `c += a + b`.

The target method writes the result into the storage that the assignment target holds; it does not assign the target. A target that is a local or an `out` parameter not assigned before the assignment (`Value c; c = a + b;`), or a field of a struct local that is not fully assigned, has no storage and is reported as GSH0003: assign it first. A target method that takes the result as an `out` parameter assigns it, so the target may be unassigned there.

The managed path of the CPU dialect expands an operator macro the same way, into the C# call of the chosen overload (its value operands cast to the overload's parameter types, so that C# calls the same overload). A `[GpuDeviceFunction]` that does not suspend runs there as the method you wrote, except one whose body expands an operator macro, or calls a device function that does: G# emits a *managed version* of it (`__gsharp_managed_{path}`, in the partial class that declares it, hidden from IntelliSense), and the managed kernels and the other managed versions call that instead. Calling such a function from host code outside a kernel launch still runs your method, whose operator throws `GpuOnlyException`.

The target method need not be accessible where the operator is used (the attribute names it): the managed path calls a target that it cannot name through a generated `[UnsafeAccessor]` stub. The one target it cannot reach is one whose declaring struct is not accessible there either (the runtime names an inaccessible type only when it is a class); that use is reported as GSH0003 on the CPU dialect and in a library that exports its device code.

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
