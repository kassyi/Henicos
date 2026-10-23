---
title: "StackLinqExtensions.Count Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Returns the number of elements in a sequence."
---

## StackLinqExtensions.Count Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the number of elements in a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [Count<TEnumerator, TSource>(TEnumerator)](#counttenumeratortsourcetenumerator) | Returns the number of elements. |
| [Count<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#counttenumeratortsourcetenumeratorfunctsourcebool) | Returns a number that represents how many elements satisfy a condition. |
| [Count<TEnumerator, TSource, TState>(TEnumerator, TState, Func<TSource, TState, bool>)](#counttenumeratortsourcetstatetenumeratortstatefunctsourcetstatebool) | Returns how many elements satisfy a condition that receives a state value. Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly. |


## Count<TEnumerator, TSource>(TEnumerator)

Returns the number of elements.

```csharp
public static int Count<TEnumerator, TSource>(this TEnumerator source) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>int</i>

## Count<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Returns a number that represents how many elements satisfy a condition.

```csharp
public static int Count<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>int</i>



## Count<TEnumerator, TSource, TState>(TEnumerator, TState, Func<TSource, TState, bool>)

Returns how many elements satisfy a condition that receives a state value. Pass the outer value as `state` so that the lambda can be `static` and no closure is allocated.

```csharp
public static int Count<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

```csharp
var result = names
    .AsStackEnumerable()
    .Count(prefix, static (n, p) => n.StartsWith(p));
```

#### Returns

<i>int</i>

**Execution Target:** Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
