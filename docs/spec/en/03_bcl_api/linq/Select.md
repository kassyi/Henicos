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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Projects each element of a sequence into a new form.

## Overloads

| Name | Description |
| --- | --- |
| [Select\<TEnumerator, TSource, TResult\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, TResult\>)](#select-overload-1) | Projects each element of a stack-allocated sequence into a new form. |
| [Select\<TEnumerator, TSource, TResult\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, TResult\>)](#select-overload-2) | Projects each element of a stack-allocated sequence into a new form by incorporating the element's index. |
| [Select\<TEnumerator, TSource, TState, TResult\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, TResult\>)](#select-overload-3) | Projects each element of a stack-allocated sequence into a new form with a selector that receives a caller-supplied state. Host-only. |

<a id="select-overload-1"></a>

## Select\<TEnumerator, TSource, TResult\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, TResult\>)

Projects each element of a stack-allocated sequence into a new form.

```csharp
public static StackEnumerable<SelectEnumerator<TEnumerator, TSource, TResult>, TResult> Select<TEnumerator, TSource, TResult>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, TResult> selector) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence of elements to invoke a transform function on.

`selector`<br>
A transform function to apply to each element.

#### Returns

A `StackEnumerable<TEnumerator, TResult>` whose elements are the result of invoking the transform function on each element of `source`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

**Performance:** Evaluated lazily with zero heap allocations.

<a id="select-overload-2"></a>

## Select\<TEnumerator, TSource, TResult\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, TResult\>)

Projects each element of a stack-allocated sequence into a new form by incorporating the element's index.

```csharp
public static StackEnumerable<SelectAtEnumerator<TEnumerator, TSource, TResult>, TResult> Select<TEnumerator, TSource, TResult>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, int, TResult> selector) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence of elements to invoke a transform function on.

`selector`<br>
A transform function to apply to each source element; the second parameter of the function represents the index of the source element.

#### Returns

A `StackEnumerable<TEnumerator, TResult>` whose elements are the result of invoking the transform function on each element of `source`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

**Performance:** Evaluated lazily with zero heap allocations.

<a id="select-overload-3"></a>

## Select\<TEnumerator, TSource, TState, TResult\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, TResult\>)

Projects each element of a stack-allocated sequence into a new form with a selector that receives a caller-supplied state.

```csharp
public static StackEnumerable<SelectWithStateEnumerator<TEnumerator, TSource, TState, TResult>, TResult> Select<TEnumerator, TSource, TState, TResult>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, TResult> selector) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence of elements to invoke a transform function on.

`state`<br>
The value passed to every call of `selector`.

`selector`<br>
A transform function to apply to each element; the second parameter is `state`.

#### Returns

A `StackEnumerable<TEnumerator, TResult>` whose elements are the result of invoking the transform function on each element of `source`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `selector` is `null`.

**Performance:** Evaluated lazily. Passing the outer value as `state` lets `selector` be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.

## Examples

```csharp
using Kassyi.ZeroAlloc.Linq;

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
- [SelectWithStateEnumerator&lt;TEnumerator, TSource, TState, TResult&gt; Struct](SelectWithStateEnumerator.md)
