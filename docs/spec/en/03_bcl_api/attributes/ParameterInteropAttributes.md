---
title: "System.Runtime.InteropServices: [In] and [Out] Attributes"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Standard .NET attributes used in G# to optimize memory transfers across the PCIe bus."
---

The standard .NET **`[In]`** and **`[Out]`** attributes from `System.Runtime.InteropServices` can be applied to array parameters in `[GpuKernel]` signatures to optimize memory transfers across the PCIe bus.

## Syntax

```csharp
// Execution Context: G# Kernel (GPU)
using System.Runtime.InteropServices;

[GpuKernel]
public static void VectorAdd([In] float[] a, [In] float[] b, [Out] float[] c, int n)
```

## Remarks

- **Default Behavior (`[In, Out]`)**: By default, G# assumes all arrays are read/write. It generates AOT wrapper code that copies data both to the device before kernel execution (`cuMemcpyHtoD`) and back to the host after execution (`cuMemcpyDtoH`).
- **`[In]` Attribute**: Indicates the array is read-only. The AOT transpiler will **skip copying the array back** to the host after execution, saving valuable PCIe bandwidth.
  A kernel write to an `[In]` array therefore never reaches the caller, and neither does a write to an array that its elements hold. The CPU paths (the native launcher and the managed fallback) give the kernel a copy of the array and of every array its elements hold, so that they produce the same result. A read-only container (`ReadOnlySpan<T>`, `ReadOnlyMemory<T>`, `ImmutableArray<T>`) is never copied back either, so the same holds for the arrays its elements hold.
- **`[Out]` Attribute**: Indicates the array is write-only. The AOT transpiler will **skip copying the array to** the device before execution. It still allocates the required memory on the device (`cuMemAlloc`).
- **One array passed to several parameters**: In C# two parameters that receive the same array refer to one object, so
  a write through one of them is seen through the other. The launcher keeps this: every `T[]` argument, and every struct
  field or converted element, that holds the same array (compared by reference, whatever the element type: an `int[]`
  passed again as a `uint[]` is the same array) shares one device region. The region is copied to the device once when
  any of its holders copies in (no `[Out]`), and back to the host once when any of them copies back (no `[In]`, not a
  read-only container). For example, `K([In] int[] a, int[] b)` called as `K(x, x)` reads through `a` what the kernel
  wrote through `b`, and `x` gets the writes. The CPU paths give the kernel the caller's array in that case; they copy
  an `[In]` array only when no other holder that sends the writes back holds it: an array argument that copies back, a
  struct argument with the array in a field at any depth, or an element of an array argument that copies back. `[In]`
  arguments that hold one array share that copy, also when one of them is a field of an element of an `[In]` container.
- **Arguments that overlap one memory**: Slices (`Span<T>`, `ReadOnlySpan<T>`, `Memory<T>`, `ArraySegment<T>`) of one
  array, a `ref` to an element and the array, or one variable passed to two `ref` parameters are one memory in C# as
  well. When a launch has such an argument beside another one, the launcher compares their addresses: arguments whose
  bytes overlap form a group, which gets one device region that each member addresses at its offset. The group is one
  holder: it is copied in when any member copies in and copied back when any member copies back, so an `[In]` member
  keeps the kernel's writes from the caller only when every member of its group is `[In]`. Views with different element
  types share the bytes as on the host. A group of elements whose host and device layouts differ (a struct that the
  launcher converts) is converted as a whole, element by element, into one buffer in the device layout, which every
  member addresses at its element offset; a struct passed to two `ref` parameters is the same. A launch without
  overlapping arguments allocates and copies as before. Three forms cannot share one region and throw
  `NotSupportedException` naming the arguments: converted members of different element types, or a converted member
  beside one whose bytes the device reads as they are (their layouts differ in different ways); converted members that
  do not start at element boundaries of each other; and members that overlap at offsets no device address can align
  for all of their element types (the device alignment of each element type counts, not its size). The CPU paths pass the caller's memory, and give the `[In]` members of a group
  of `[In]` arguments one shared copy. When every member of such a group is a `Span<T>` or `ReadOnlySpan<T>`, the copy
  is one buffer of bytes that each member views, so views of different element types work as on the device. A group of
  `[In]` arguments that also holds a `T[]`, `Memory<T>`, `ArraySegment<T>` or `ImmutableArray<T>` needs one typed
  copy; with members of different element types it throws `NotSupportedException` on the CPU paths only.

Using these attributes appropriately is highly recommended for performance-critical kernels, as redundant PCIe transfers can drastically degrade performance compared to native C++ CUDA.

## Examples

### Hash Kernel Data Transfer Optimization

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using System.Reflection;
using System.Runtime.InteropServices;
using Kassyi.GSharp.Core;

// Execution Context: Host (CPU) Transpiler Inspection

Example.Run();

public class Example
{
    [GpuKernel]
    public static unsafe void HashKernel([In] byte[] input, [Out] uint[] output, int numMessages)
    {
        // The AOT wrapper will only copy 'input' to the GPU, and only copy 'output' back to the CPU.
    }

    public static void Run()
    {
        var method = typeof(Example).GetMethod(nameof(HashKernel));
        
        var inputParam = method.GetParameters()[0];
        var outputParam = method.GetParameters()[1];

        Console.WriteLine($"Parameter '{inputParam.Name}' has [In]: {inputParam.GetCustomAttribute<InAttribute>() != null}");
        Console.WriteLine($"Parameter '{outputParam.Name}' has [Out]: {outputParam.GetCustomAttribute<OutAttribute>() != null}");
    }
}
```

## See also



### Language Specification

- [1. Language Specification](../../01_language/00_index.md)
- [2. Type System and Mapping](../../01_language/03_type_system.md)

### Architecture

- [2. Compiler and Runtime Architecture](../../02_architecture/00_index.md)
- [8. Comprehensive Example](../../02_architecture/03_comprehensive_example.md)

### BCL API > Memory

- [Memory Management](../memory/00_index.md)

### Guides

- [4. Practical Guides and Best Practices](../../04_guides/00_index.md)
- [11. Strict Memory Management and Ownership Patterns (Zero-Leak Analyzer)](../../04_guides/02_memory_safety_patterns.md)

