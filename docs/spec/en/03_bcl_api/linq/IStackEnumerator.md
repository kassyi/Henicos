---
title: "IStackEnumerator<T> Interface (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Defines a generalized, zero-allocation, forward-only stack enumerator interface."
---

## IStackEnumerator&lt;T&gt; Interface

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Defines a generalized, zero-allocation, forward-only stack enumerator interface.

```csharp
public interface IStackEnumerator<T>
```

#### Type Parameters

`T`<br>
The type of objects to enumerate.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out T)](#trygetnextout-t) | Attempts to advance the enumerator to the next element of the sequence and outputs the current element if available. |

## TryGetNext(out T)

Attempts to advance the enumerator to the next element of the sequence and outputs the current element if available.

```csharp
bool TryGetNext(out T current);
```

#### Parameters

`current` _T_<br>
When this method returns `true`, contains the next element in the sequence; otherwise, the default value of `T`.

#### Returns

_Boolean_<br>
`true` if the enumerator was successfully advanced to the next element; `false` if the enumerator has passed the end of the sequence.

## Remarks

`IStackEnumerator<T>` is designed to be implemented by `ref struct` or value-type `struct` enumerators constrained with `where TEnumerator : struct, IStackEnumerator<T>, allows ref struct` to guarantee zero heap allocations and aggressive JIT/NVCC inlining during iteration. Enumerators are mutable stateful value types and are not thread-safe.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

static int SumAll<TEnumerator>(TEnumerator enumerator)
    where TEnumerator : struct, IStackEnumerator<int>, allows ref struct
{
    int total = 0;
    while (enumerator.TryGetNext(out int item))
    {
        total += item;
    }
    return total;
}

ReadOnlySpan<int> data = [1, 2, 3, 4];
Console.WriteLine($"Total: {SumAll(data.AsStackEnumerable().Enumerator)}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [ArrayEnumerator&lt;T&gt; Struct](ArrayEnumerator.md)
- [AsStackEnumerable Method](AsStackEnumerable.md)
