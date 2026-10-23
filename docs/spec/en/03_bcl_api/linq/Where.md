---
title: "StackLinqExtensions.Where Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Filters a sequence of values based on a predicate."
---

## StackLinqExtensions.Where Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Filters a sequence of values based on a predicate.

## Overloads

| Name | Description |
| --- | --- |
| [Where<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#wheretenumeratortsourcetenumeratorfunctsourcebool) | Filters a sequence based on a predicate. |
| [Where<TEnumerator, TSource>(TEnumerator, Func<TSource, int, bool>)](#wheretenumeratortsourcetenumeratorfunctsourceintbool) | Filters a sequence based on a predicate that incorporates the element's index. |
| [Where<TEnumerator, TSource, TState>(TEnumerator, TState, Func<TSource, TState, bool>)](#wheretenumeratortsourcetstatetenumeratortstatefunctsourcetstatebool) | Filters a sequence based on a predicate that receives a state value. Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly. |


## Where<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Filters a sequence based on a predicate.

```csharp
public static WhereEnumerator<TEnumerator, TSource> Where<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>WhereEnumerator<TEnumerator, TSource></i>

## Where<TEnumerator, TSource>(TEnumerator, Func<TSource, int, bool>)

Filters a sequence based on a predicate that incorporates the element's index.

```csharp
public static WhereAtEnumerator<TEnumerator, TSource> Where<TEnumerator, TSource>(this TEnumerator source, Func<TSource, int, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>WhereAtEnumerator<TEnumerator, TSource></i>



## Where<TEnumerator, TSource, TState>(TEnumerator, TState, Func<TSource, TState, bool>)

Filters a sequence based on a predicate that receives a state value. Pass the outer value as `state` so that the lambda can be `static` and no closure is allocated.

```csharp
public static StackEnumerable<WhereWithStateEnumerator<TEnumerator, TSource, TState>, TSource> Where<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

```csharp
var result = names
    .AsStackEnumerable()
    .Where(prefix, static (n, p) => n.StartsWith(p));
```

#### Returns

<i>StackEnumerable&lt;WhereWithStateEnumerator&lt;TEnumerator, TSource, TState&gt;, TSource&gt;</i>

**Execution Target:** Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly.

## Examples

```csharp
using Kassyi.ZeroAlloc.Linq;

int[] numbers = [1, 2, 3, 4, 5, 6];

// Standard predicate
var evens = numbers
    .AsStackEnumerable()
    .Where(x => x % 2 == 0);

// Indexed predicate (keep even values at odd indices)
var filtered = numbers
    .AsStackEnumerable()
    .Where((val, index) => val % 2 == 0 && index % 2 != 0);
```

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [WhereWithStateEnumerator&lt;TEnumerator, TSource, TState&gt; Struct](WhereWithStateEnumerator.md)
