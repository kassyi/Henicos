---
title: "StackLinqExtensions.All Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Determines whether all elements of a sequence satisfy a condition."
---

## StackLinqExtensions.All Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Determines whether all elements of a sequence satisfy a condition.

## Overloads

| Name | Description |
| --- | --- |
| [All\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#all-overload-1) | Determines whether all elements of a stack-allocated sequence satisfy a condition. |
| [All\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)](#all-overload-2) | Determines whether all elements satisfy a condition that receives a caller-supplied state. Host-only. |

<a id="all-overload-1"></a>

## All\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Determines whether all elements of a stack-allocated sequence satisfy a condition.

```csharp
public static bool All<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence whose elements to apply the predicate to.

`predicate`<br>
A function to test each element for a condition.

#### Returns

`true` if every element of the source sequence passes the test in the specified predicate, or if the sequence is empty; otherwise, `false`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Short-circuits upon the first non-matching element with zero heap allocations.

<a id="all-overload-2"></a>

## All\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)

Determines whether all elements satisfy a condition that receives a caller-supplied state.

```csharp
public static bool All<TEnumerator, TSource, TState>(
    this StackEnumerable<TEnumerator, TSource> source,
    TState state,
    Func<TSource, TState, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence whose elements to apply the predicate to.

`state`<br>
The value passed to every call of `predicate`.

`predicate`<br>
A function to test each element for a condition; the second parameter is `state`.

#### Returns

`true` if every element passes the test, or if the sequence is empty; otherwise, `false`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Short-circuits upon the first non-matching element. Passing the outer value as `state` lets `predicate` be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
