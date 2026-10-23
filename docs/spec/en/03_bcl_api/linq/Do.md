---
title: "StackLinqExtensions.Do Method (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Runs an action on every element of a stack-allocated sequence."
---

## StackLinqExtensions.Do Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Runs an action on every element of a stack-allocated sequence.

## Overloads

| Name | Description |
| --- | --- |
| [Do&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Action&lt;TSource&gt;)](#dotenumerator-tsourcestackenumerabletenumerator-tsource-actiontsource) | Runs an action on every element. |
| [Do&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TState, Action&lt;TSource, TState&gt;)](#dotenumerator-tsource-tstatestackenumerabletenumerator-tsource-tstate-actiontsource-tstate) | Runs an action that receives a state value on every element. |

## Do&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Action&lt;TSource&gt;)

Runs an action on every element of a stack-allocated sequence.

```csharp
public static void Do<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, Action<TSource> action)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence whose elements are visited.

`action` _Action&lt;TSource&gt;_<br>
The action to run on each element.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `action` is `null`.

## Do&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TState, Action&lt;TSource, TState&gt;)

Runs an action that receives a caller-supplied state on every element. Pass the outer value as `state` so that the action can be a `static` lambda and no closure is allocated.

```csharp
public static void Do<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Action<TSource, TState> action)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence whose elements are visited.

`state` _TState_<br>
The value passed to every call of `action`.

`action` _Action&lt;TSource, TState&gt;_<br>
The action to run on each element; the second parameter is `state`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `action` is `null`.

## Remarks

`Do` ends a query the way a `foreach` over its results would: it consumes the sequence and returns nothing. Use it for side effects such as writing each element to a collection that is not an `ICollection<T>` or reporting a diagnostic per element. To act only on some elements, use [DoIf](DoIf.md); to add to an `ICollection<T>`, use [AddTo](AddTo.md).

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for both overloads.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using System.Text;
using Kassyi.ZeroAlloc.Linq;

string[] names = ["alpha", "beta", "gamma"];
var builder = new StringBuilder();

names
    .AsStackEnumerable()
    .Do(builder, static (name, b) => b.Append(name[0]));

Console.WriteLine($"Initials: {builder}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackLinqExtensions Class](StackLinqExtensions.md)
- [DoIf Method](DoIf.md)
- [AddTo Method](AddTo.md)
