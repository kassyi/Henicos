---
title: "ArrayEnumerator<T> Struct (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Provides a zero-allocation, forward-only stack enumerator over a managed array."
---

## ArrayEnumerator&lt;T&gt; Struct

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Provides a zero-allocation, forward-only stack enumerator over a managed array.

```csharp
public struct ArrayEnumerator<T> : IStackEnumerator<T>
```

#### Type Parameters

`T`<br>
The type of elements in the array.

## Constructors

| Name | Description |
| --- | --- |
| [ArrayEnumerator(T[])](#arrayenumeratort) | Initializes a new instance of the `ArrayEnumerator<T>` struct wrapping the specified array. |

## ArrayEnumerator(T[])

Initializes a new instance of the `ArrayEnumerator<T>` struct wrapping the specified array.

```csharp
public ArrayEnumerator(T[] source);
```

#### Parameters

`source` _T[]_<br>
The source array to enumerate.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `source` is `null`.

## Methods

| Name | Description |
| --- | --- |
| [TryGetNext(out T)](#trygetnextout-t) | Attempts to advance the enumerator to the next element of the array. |

## TryGetNext(out T)

Attempts to advance the enumerator to the next element of the array.

```csharp
public bool TryGetNext(out T current);
```

#### Parameters

`current` _T_<br>
When this method returns `true`, contains the element at the current index; otherwise, the default value of `T`.

#### Returns

_Boolean_<br>
`true` if the enumerator successfully advanced to the next element; `false` if the end of the array was reached.

## Remarks

`ArrayEnumerator<T>` guarantees zero heap allocation ($O(1)$ space) during enumeration and can be created either directly or via [StackLinqExtensions.AsStackEnumerable](AsStackEnumerable.md).

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU) / Device (GPU)
using System;
using Kassyi.ZeroAlloc.Linq;

int[] numbers = [10, 20, 30];
var enumerator = new ArrayEnumerator<int>(numbers);

int sum = 0;
while (enumerator.TryGetNext(out int value))
{
    sum += value;
}

Console.WriteLine($"Sum: {sum}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [IStackEnumerator&lt;T&gt; Interface](IStackEnumerator.md)
- [AsStackEnumerable Method](AsStackEnumerable.md)
