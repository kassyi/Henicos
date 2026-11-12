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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Filters a sequence of values based on a predicate.

## Overloads

| Name | Description |
| --- | --- |
| [Where\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#where-overload-1) | Filters a stack-allocated sequence of values based on a predicate. |
| [Where\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, bool\>)](#where-overload-2) | Filters a stack-allocated sequence of values based on a predicate. Each element's index is used in the logic of the predicate function. |
| [Where\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)](#where-overload-3) | Filters a stack-allocated sequence of values based on a predicate that receives a caller-supplied state. Host-only. |

<a id="where-overload-1"></a>

## Where\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Filters a stack-allocated sequence of values based on a predicate.

```csharp
public static StackEnumerable<WhereEnumerator<TEnumerator, TSource>, TSource> Where<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to filter.

`predicate`<br>
A function to test each element for a condition.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains elements from the input sequence that satisfy the condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Evaluated lazily with zero heap allocations.

<a id="where-overload-2"></a>

## Where\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, bool\>)

Filters a stack-allocated sequence of values based on a predicate. Each element's index is used in the logic of the predicate function.

```csharp
public static StackEnumerable<WhereAtEnumerator<TEnumerator, TSource>, TSource> Where<TEnumerator, TSource>(
    this StackEnumerable<TEnumerator, TSource> source,
    Func<TSource, int, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to filter.

`predicate`<br>
A function to test each source element for a condition; the second parameter of the function represents the index of the source element.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains elements from the input sequence that satisfy the condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Evaluated lazily with zero heap allocations.

<a id="where-overload-3"></a>

## Where\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)

Filters a stack-allocated sequence of values based on a predicate that receives a caller-supplied state.

```csharp
public static StackEnumerable<WhereWithStateEnumerator<TEnumerator, TSource, TState>, TSource> Where<TEnumerator,
    TSource, TState>(
    this StackEnumerable<TEnumerator, TSource> source,
    TState state,
    Func<TSource, TState, bool> predicate)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The source sequence to filter.

`state`<br>
The value passed to every call of `predicate`.

`predicate`<br>
A function to test each element for a condition; the second parameter is `state`.

#### Returns

A `StackEnumerable<TEnumerator, TSource>` that contains elements from the input sequence that satisfy the condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Evaluated lazily. Passing the outer value as `state` lets `predicate` be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.

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
