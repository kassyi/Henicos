---
title: "StackLinqExtensions.DoIf Method (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Runs an action on the elements of a stack-allocated sequence that satisfy a condition."
---

## StackLinqExtensions.DoIf Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Runs an action on the elements of a stack-allocated sequence that satisfy a condition.

## Overloads

| Name | Description |
| --- | --- |
| [DoIf&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;, Action&lt;TSource&gt;)](#doiftenumerator-tsourcestackenumerabletenumerator-tsource-functsource-boolean-actiontsource) | Runs an action on the elements that satisfy a condition. |
| [DoIf&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TState, Func&lt;TSource, TState, Boolean&gt;, Action&lt;TSource, TState&gt;)](#doiftenumerator-tsource-tstatestackenumerabletenumerator-tsource-tstate-functsource-tstate-boolean-actiontsource-tstate) | Runs an action on the elements that satisfy a condition; both receive a state value. |

## DoIf&lt;TEnumerator, TSource&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, Func&lt;TSource, Boolean&gt;, Action&lt;TSource&gt;)

Runs an action on the elements of a stack-allocated sequence that satisfy a condition.

```csharp
public static void DoIf<TEnumerator, TSource>(this StackEnumerable<TEnumerator, TSource> source, Func<TSource, bool> predicate, Action<TSource> action)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence whose elements are tested.

`predicate` _Func&lt;TSource, Boolean&gt;_<br>
A function to test each element for a condition.

`action` _Action&lt;TSource&gt;_<br>
The action to run on each matching element.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` or `action` is `null`.

## DoIf&lt;TEnumerator, TSource, TState&gt;(StackEnumerable&lt;TEnumerator, TSource&gt;, TState, Func&lt;TSource, TState, Boolean&gt;, Action&lt;TSource, TState&gt;)

Runs an action on the elements that satisfy a condition; the predicate and the action both receive a caller-supplied state, so that both can be `static` lambdas.

```csharp
public static void DoIf<TEnumerator, TSource, TState>(this StackEnumerable<TEnumerator, TSource> source, TState state, Func<TSource, TState, bool> predicate, Action<TSource, TState> action)
    where TEnumerator : struct, IStackEnumerator<TSource>, allows ref struct;
```

#### Parameters

`source` _StackEnumerable&lt;TEnumerator, TSource&gt;_<br>
The sequence whose elements are tested.

`state` _TState_<br>
The value passed to every call of `predicate` and `action`.

`predicate` _Func&lt;TSource, TState, Boolean&gt;_<br>
A function to test each element for a condition; the second parameter is `state`.

`action` _Action&lt;TSource, TState&gt;_<br>
The action to run on each matching element; the second parameter is `state`.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `predicate` or `action` is `null`.

## Remarks

`DoIf(predicate, action)` behaves as `Where(predicate).Do(action)` in one call. The state overload exists because the state forms of `Where` and `Do` would otherwise need the state twice.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for both overloads.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using System.Collections.Generic;
using Kassyi.ZeroAlloc.Linq;

int[] scores = [72, 45, 90, 38, 66];
var report = new List<string>();

scores
    .AsStackEnumerable()
    .DoIf(
        (Limit: 50, Report: report),
        static (s, st) => s < st.Limit,
        static (s, st) => st.Report.Add($"{s} is below {st.Limit}")
    );

Console.WriteLine(string.Join("; ", report));
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackLinqExtensions Class](StackLinqExtensions.md)
- [Do Method](Do.md)
- [AddIf Method](AddIf.md)
