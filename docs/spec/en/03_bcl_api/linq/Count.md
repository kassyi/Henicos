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
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Returns the number of elements in a sequence.

## Overloads

| Name | Description |
| --- | --- |
| [Count\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)](#count-overload-1) | Returns the number of elements in a stack-allocated sequence. |
| [Count\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)](#count-overload-2) | Returns a number that represents how many elements in the specified stack-allocated sequence satisfy a condition. |
| [Count\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, bool\>)](#count-overload-3) | Returns a number that represents how many elements in the specified stack-allocated sequence satisfy a condition. Each element's index is used in the logic of the predicate function. |
| [Count\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)](#count-overload-4) | Returns a number that represents how many elements satisfy a condition that receives a caller-supplied state. Host-only. |

<a id="count-overload-1"></a>

## Count\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>)

Returns the number of elements in a stack-allocated sequence.

```csharp
public static int Count<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence that contains elements to be counted.

#### Returns

The number of elements in the input sequence.

**Performance:** Consumes the enumerator in $O(N)$ time with zero heap allocations.

<a id="count-overload-2"></a>

## Count\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, bool\>)

Returns a number that represents how many elements in the specified stack-allocated sequence satisfy a condition.

```csharp
public static int Count<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence that contains elements to be counted.

`predicate`<br>
A function to test each element for a condition.

#### Returns

A number that represents how many elements in the sequence satisfy the condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Consumes the enumerator in $O(N)$ time with zero heap allocations.

<a id="count-overload-3"></a>

## Count\<TEnumerator, TSource\>(StackEnumerable\<TEnumerator, TSource\>, Func\<TSource, int, bool\>)

Returns a number that represents how many elements in the specified stack-allocated sequence satisfy a condition. Each element's index is used in the logic of the predicate function.

```csharp
public static int Count<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, int, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence that contains elements to be counted.

`predicate`<br>
A function to test each element for a condition; the second parameter of the function represents the index of the source element.

#### Returns

A number that represents how many elements in the sequence satisfy the condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Consumes the enumerator in $O(N)$ time with zero heap allocations.

<a id="count-overload-4"></a>

## Count\<TEnumerator, TSource, TState\>(StackEnumerable\<TEnumerator, TSource\>, TState, Func\<TSource, TState, bool\>)

Returns a number that represents how many elements satisfy a condition that receives a caller-supplied state.

```csharp
public static int Count<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, bool> predicate) where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source`<br>
The sequence that contains elements to be counted.

`state`<br>
The value passed to every call of `predicate`.

`predicate`<br>
A function to test each element for a condition; the second parameter is `state`.

#### Returns

A number that represents how many elements in the sequence satisfy the condition.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` is `null`.

**Performance:** Passing the outer value as `state` lets `predicate` be a `static` lambda, so no closure is allocated.

**Execution Target:** Host-only (CPU) operation. Cannot be transpiled or invoked within GPU kernels.

## Remarks

This extension method guarantees zero heap allocations by returning a `ref struct` enumerator (or scalar) that is fully inlineable by the G# Transpiler.

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
