---
title: "StackLinqExtensions.Any Method (G#)"
site: "G# Documentation"
published: "2026-10-09T00:00:00.000Z"
language: "en"
description: "Determines whether any element of a sequence exists or satisfies a condition."
---

## StackLinqExtensions.Any Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Determines whether any element of a sequence exists or satisfies a condition.

## Overloads

| Name | Description |
| --- | --- |
| [Any\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)](#any-overload-1) | Determines whether a stack-allocated sequence contains any elements. |
| [Any\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#any-overload-2) | Determines whether any element of a stack-allocated sequence satisfies a condition. |
| [Any\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)](#any-overload-3) | Determines whether any element satisfies a condition that receives a caller-supplied state. Host-only. |

<a id="any-overload-1"></a>

## Any\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)

Determines whether a stack-allocated sequence contains any elements.

```csharp
public static bool Any<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to check for non-emptiness.

#### Returns

`true` if the source sequence contains any elements; otherwise, `false`.

**Performance:** Short-circuits on the first element ($O(1)$) with zero heap allocations.

<a id="any-overload-2"></a>

## Any\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Determines whether any element of a stack-allocated sequence satisfies a condition.

```csharp
public static bool Any<TEnumerator, TSource>(
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

`true` if any element satisfies the condition; otherwise, `false`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Short-circuits upon the first matching element with zero heap allocations.

<a id="any-overload-3"></a>

## Any\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)

Determines whether any element satisfies a condition that receives a caller-supplied state.

```csharp
public static bool Any<TEnumerator, TSource, TState>(
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

`true` if any element satisfies the condition; otherwise, `false`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Short-circuits upon the first matching element. Passing the outer value as `state` lets `predicate` be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
