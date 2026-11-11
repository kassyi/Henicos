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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Returns the first element of a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [First\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)](#first-overload-1) | Returns the first element of a stack-allocated sequence. |
| [First\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#first-overload-2) | Returns the first element in a stack-allocated sequence that satisfies a specified condition. |
| [First\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)](#first-overload-3) | Returns the first element that satisfies a condition that receives a caller-supplied state. Host-only. |
| [FirstOrDefault\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)](#first-overload-4) | Returns the first element of a stack-allocated sequence, or a default value if the sequence contains no elements. |
| [FirstOrDefault\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#first-overload-5) | Returns the first element of the sequence that satisfies a condition, or a default value if no such element is found. |
| [FirstOrDefault\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)](#first-overload-6) | Returns the first element that satisfies a condition that receives a caller-supplied state, or a default value if no such element is found. Host-only. |

<a id="first-overload-1"></a>

## First\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)

Returns the first element of a stack-allocated sequence.

```csharp
public static TSource First<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to return the first element of.

#### Returns

The first element in the sequence.

#### Exceptions

`InvalidOperationException`<br>
Thrown when the source sequence is empty.

**Performance:** Operates in $O(1)$ time with zero heap allocations.

<a id="first-overload-2"></a>

## First\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Returns the first element in a stack-allocated sequence that satisfies a specified condition.

```csharp
public static TSource First<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to return an element from.

`predicate`<br>
A function to test each element for a condition.

#### Returns

The first element in the sequence that matches the condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

`InvalidOperationException`<br>
Thrown when no element satisfies the condition, or if the source sequence is empty.

**Performance:** Short-circuits upon the first match with zero heap allocations.

<a id="first-overload-3"></a>

## First\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)

Returns the first element that satisfies a condition that receives a caller-supplied state.

```csharp
public static TSource First<TEnumerator, TSource, TState>(
    this StackEnumerable<TEnumerator, TSource> source,
    TState state,
    Func<TSource, TState, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to return an element from.

`state`<br>
The value passed to every call of `predicate`.

`predicate`<br>
A function to test each element for a condition; the second parameter is `state`.

#### Returns

The first element in the sequence that matches the condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

`InvalidOperationException`<br>
Thrown when no element satisfies the condition, or if the source sequence is empty.

**Performance:** Short-circuits upon the first match. Passing the outer value as `state` lets `predicate` be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.

<a id="first-overload-4"></a>

## FirstOrDefault\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)

Returns the first element of a stack-allocated sequence, or a default value if the sequence contains no elements.

```csharp
public static TSource? FirstOrDefault<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to return the first element of.

#### Returns

The default value of `TSource` if `source` is empty; otherwise, the first element.

**Performance:** Operates in $O(1)$ time with zero heap allocations.

<a id="first-overload-5"></a>

## FirstOrDefault\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Returns the first element of the sequence that satisfies a condition, or a default value if no such element is found.

```csharp
public static TSource? FirstOrDefault<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to return an element from.

`predicate`<br>
A function to test each element for a condition.

#### Returns

The default value of `TSource` if `source` is empty or if no element passes the test; otherwise, the first element that passes.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Short-circuits upon the first matching element with zero heap allocations.

<a id="first-overload-6"></a>

## FirstOrDefault\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)

Returns the first element that satisfies a condition that receives a caller-supplied state, or a default value if no such element is found.

```csharp
public static TSource? FirstOrDefault<TEnumerator, TSource, TState>(
    this StackEnumerable<TEnumerator, TSource> source,
    TState state,
    Func<TSource, TState, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence to return an element from.

`state`<br>
The value passed to every call of `predicate`.

`predicate`<br>
A function to test each element for a condition; the second parameter is `state`.

#### Returns

The default value of `TSource` if no element passes the test; otherwise, the first element that passes.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Short-circuits upon the first match. Passing the outer value as `state` lets `predicate` be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
