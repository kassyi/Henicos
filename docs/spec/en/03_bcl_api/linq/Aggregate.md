---
title: "StackLinqExtensions.Aggregate Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Applies an accumulator function over a sequence."
---

## StackLinqExtensions.Aggregate Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Applies an accumulator function over a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [Aggregate\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, TSource, TSource\>)](#aggregate-overload-1) | Applies an accumulator function over a sequence. |
| [Aggregate\<TEnumerator, TSource, TAccumulate\>(StackEnumerable\<TEnumerator, TSource\>, TAccumulate, Func\<TAccumulate, TSource, TAccumulate\>)](#aggregate-overload-2) | Applies an accumulator function over a stack-allocated sequence. The specified seed value is used as the initial accumulator value. |

<a id="aggregate-overload-1"></a>

## Aggregate\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, TSource, TSource\>)

Applies an accumulator function over a sequence.

```csharp
public static TSource Aggregate<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, TSource, TSource> func)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
A sequence to aggregate over.

`func`<br>
An accumulator function to be invoked on each element.

#### Returns

The final accumulator value.

#### Exceptions

`ArgumentNullException`<br>
`func` is `null`.

`InvalidOperationException`<br>
The source sequence is empty.

<a id="aggregate-overload-2"></a>

## Aggregate\<TEnumerator, TSource, TAccumulate\>(StackEnumerable\<TEnumerator, TSource\>, TAccumulate, Func\<TAccumulate, TSource, TAccumulate\>)

Applies an accumulator function over a stack-allocated sequence. The specified seed value is used as the initial accumulator value.

```csharp
public static TAccumulate Aggregate<TEnumerator, TSource, TAccumulate>(
    this StackEnumerable<TEnumerator, TSource> source,
    TAccumulate seed,
    Func<TAccumulate, TSource, TAccumulate> func)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
A stack-allocated sequence to aggregate over.

`seed`<br>
The initial accumulator value.

`func`<br>
An accumulator function to be invoked on each element.

#### Returns

The final accumulator value.

#### Exceptions

`ArgumentNullException`<br>
`func` is `null`.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
