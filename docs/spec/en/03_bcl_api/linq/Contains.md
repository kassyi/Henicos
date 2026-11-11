---
title: "StackLinqExtensions.Contains Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Determines whether a sequence contains a specified element."
---

## StackLinqExtensions.Contains Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Determines whether a sequence contains a specified element.

## Overloads

| Name | Description |
| --- | --- |
| [Contains\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, TSource)](#contains-overload-1) | Determines whether a stack-allocated sequence contains a specified element by using the default equality comparer. |

<a id="contains-overload-1"></a>

## Contains\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, TSource)

Determines whether a stack-allocated sequence contains a specified element by using the default equality comparer.

```csharp
public static bool Contains<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, TSource value) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence in which to locate a value.

`value`<br>
The value to locate in the sequence.

#### Returns

`true` if the source sequence contains an element with the specified value; otherwise, `false`.

**Performance:** Linear search in $O(N)$ time with zero heap allocations.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
