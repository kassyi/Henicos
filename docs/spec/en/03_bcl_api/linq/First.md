---
title: "StackLinqExtensions.First Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Returns the first element of a sequence."
---

## StackLinqExtensions.First Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Returns the first element of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [First<TEnumerator, TSource>(TEnumerator)](#firsttenumeratortsourcetenumerator) | Returns the first element. |
| [First<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)](#firsttenumeratortsourcetenumeratorfunctsourcebool) | Returns the first element that satisfies a specified condition. |
| [First<TEnumerator, TSource, TState>(TEnumerator, TState, Func<TSource, TState, bool>)](#firsttenumeratortsourcetstatetenumeratortstatefunctsourcetstatebool) | Returns the first element that satisfies a condition that receives a state value. Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly. |
| [FirstOrDefault<TEnumerator, TSource, TState>(TEnumerator, TState, Func<TSource, TState, bool>)](#firstordefaulttenumeratortsourcetstatetenumeratortstatefunctsourcetstatebool) | Returns the first element that satisfies a condition that receives a state value, or the default value. Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly. |


## First<TEnumerator, TSource>(TEnumerator)

Returns the first element.

```csharp
public static TSource First<TEnumerator, TSource>(this TEnumerator source) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TSource</i>

## First<TEnumerator, TSource>(TEnumerator, Func<TSource, bool>)

Returns the first element that satisfies a specified condition.

```csharp
public static TSource First<TEnumerator, TSource>(this TEnumerator source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>TSource</i>



## First<TEnumerator, TSource, TState>(TEnumerator, TState, Func<TSource, TState, bool>)

Returns the first element that satisfies a condition that receives a state value. Pass the outer value as `state` so that the lambda can be `static` and no closure is allocated.

```csharp
public static TSource First<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

```csharp
var result = names
    .AsStackEnumerable()
    .First(prefix, static (n, p) => n.StartsWith(p));
```

#### Returns

<i>TSource</i>

**Execution Target:** Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly.

## FirstOrDefault<TEnumerator, TSource, TState>(TEnumerator, TState, Func<TSource, TState, bool>)

Returns the first element that satisfies a condition that receives a state value, or the default value. Pass the outer value as `state` so that the lambda can be `static` and no closure is allocated.

```csharp
public static TSource? FirstOrDefault<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

```csharp
var result = names
    .AsStackEnumerable()
    .FirstOrDefault(prefix, static (n, p) => n.StartsWith(p));
```

#### Returns

<i>TSource?</i>

**Execution Target:** Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
