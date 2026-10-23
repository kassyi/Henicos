---
title: "StackLinqExtensions.Select Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Projects each element of a sequence into a new form."
---

## StackLinqExtensions.Select Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Projects each element of a sequence into a new form.

## Overloads

| Name | Description |
| --- | --- |
| [Select<TEnumerator, TSource, TResult>(TEnumerator, Func<TSource, TResult>)](#selecttenumeratortsourcetresulttenumeratorfunctsourcetresult) | Projects elements into a new form. |
| [Select<TEnumerator, TSource, TResult>(TEnumerator, Func<TSource, int, TResult>)](#selecttenumeratortsourcetresulttenumeratorfunctsourceinttresult) | Projects elements into a new form incorporating index. |
| [Select<TEnumerator, TSource, TState, TResult>(TEnumerator, TState, Func<TSource, TState, TResult>)](#selecttenumeratortsourcetstatetresulttenumeratortstatefunctsourcetstatetresult) | Projects elements into a new form with a selector that receives a state value. Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly. |


## Select<TEnumerator, TSource, TResult>(TEnumerator, Func<TSource, TResult>)

Projects elements into a new form.

```csharp
public static SelectEnumerator<TEnumerator, TSource, TResult> Select<TEnumerator, TSource, TResult>(this TEnumerator source, Func<TSource, TResult> selector) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>SelectEnumerator<TEnumerator, TSource, TResult></i>

## Select<TEnumerator, TSource, TResult>(TEnumerator, Func<TSource, int, TResult>)

Projects elements into a new form incorporating index.

```csharp
public static SelectAtEnumerator<TEnumerator, TSource, TResult> Select<TEnumerator, TSource, TResult>(this TEnumerator source, Func<TSource, int, TResult> selector) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Returns

<i>SelectAtEnumerator<TEnumerator, TSource, TResult></i>



## Select<TEnumerator, TSource, TState, TResult>(TEnumerator, TState, Func<TSource, TState, TResult>)

Projects elements into a new form with a selector that receives a state value. Pass the outer value as `state` so that the lambda can be `static` and no closure is allocated.

```csharp
public static StackEnumerable<SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult>, TResult> Select<TEnumerator, TSource, TState, TResult>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, TResult> selector) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

```csharp
var lengths = names
    .AsStackEnumerable()
    .Select(prefix, static (n, p) => n.Length + p.Length);
```

#### Returns

<i>StackEnumerable&lt;SelectWithStateEnumerator&lt;TEnumerator, TSource, TState, TResult&gt;, TResult&gt;</i>

**Execution Target:** Host-only: a kernel reports `GSH0003`; a kernel lambda reads outer values directly.

## Examples

```csharp
int[] numbers = [1, 2, 3];

// Multiply by 10
var tens = numbers
    .AsStackEnumerable()
    .Select(x => x * 10);

// Offset by index
var indexed = numbers
    .AsStackEnumerable()
    .Select((x, i) => x + i);
```

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
