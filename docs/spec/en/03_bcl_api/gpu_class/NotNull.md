---
title: "Gpu.NotNull Method (G#)"
site: "G# Documentation"
published: "2026-11-12T00:00:00.000Z"
language: "en"
description: "Returns a nullable array as a non-nullable one, or throws NullReferenceException when it is null."
---

## Gpu.NotNull Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns a nullable array as a non-nullable one, or throws `NullReferenceException` when it is `null`.

## Overloads

| Name | Description |
| --- | --- |
| [NotNull&lt;T&gt;(T[]?)](#notnulltt) | Returns the array, or throws when it is `null`. |

## NotNull&lt;T&gt;(T[]?)

Returns `array` itself, typed as a non-nullable `T[]`, or throws `NullReferenceException` when it is `null`.

```csharp
public static T[] NotNull<T>([NotNull] T[]? array);
```

#### Type Parameters

`T`<br>
The element type of the array.

#### Parameters

`array` _T[]?_<br>
An array that may be `null`, such as a nullable array field of a struct argument. After the call the C# compiler
treats the argument expression as not `null` as well (`[NotNull]`).

#### Returns

_T[]_<br>
`array` itself.

#### Exceptions

`NullReferenceException`<br>
`array` is `null`. In a kernel body the exception carries the place of the call in
`Exception.Data["GSharp.SourceLocation"]` on CUDA and on the native CPU build, as the exception of any other read of a
`null` array does ([Diagnostics and Exceptions](../01_diagnostics_and_exceptions.md)); in a device function the message
names the place. The managed CPU path throws from the C# method. The message is the same on every path:
`Gpu.NotNull was given a null array` (after `G#: ` on the device).

## Examples

### Reading a nullable field once

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
#nullable enable
using Kassyi.GSharp.Core;

public struct SumContext
{
    public int[]? Data;   // may be null when the caller has nothing to pass
}

public static partial class SumKernels
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void SumKernel(SumContext ctx, int[] result)
    {
        int[] data = Gpu.NotNull(ctx.Data);   // one null check, here
        int sum = 0;
        for (int i = 0; i < data.Length; i++)
        {
            sum += data[i];                    // no null check
        }

        result[0] = sum;
    }
}
```

## Remarks

Every read of a nullable array (`T[]?`), such as `ctx.Data[i]` or `ctx.Data.Length`, carries a null check, because C#
throws `NullReferenceException` there ([6.4 The cost of following C#](../../01_language/06_semantics.md)).
`Gpu.NotNull` checks once and hands back an array that the compiler knows is not `null`: a local initialized from the
call and never assigned afterwards is proven, so its reads and the calls that pass it where a device function parameter
is not nullable have no check of their own. A call whose argument is already proven (a non-nullable device function
parameter that the function never writes, for example) emits no check either.

The exception comes at the call, which may be earlier than the first read of the array in the code that does not use
the method; it is the same exception type.

A kernel array parameter may be `null` whatever its annotation, as in a C# call, so every read of it is checked;
`int[] data = Gpu.NotNull(input);` at the top of the kernel checks it once and leaves the later reads of `data`
without a check. An empty array is not `null` and passes, as an argument, in a struct field or in a local.

The method runs on the host as well, where it is plain C#.

## See also

### Language Specification

- [6. Semantics Charter](../../01_language/06_semantics.md)

### BCL API > Gpu Class

- [Gpu Class](00_index.md)

### Guides

- [Deep Struct Marshaling Guide](../../04_guides/11_deep_struct_marshaling_guide.md)
