---
title: "StackEnumerable.OfType Method (G#)"
site: "G# Documentation"
published: "2026-10-23T00:00:00.000Z"
language: "en"
description: "Filters a stack-allocated sequence down to the elements of a given type."
---

## StackEnumerable&lt;TEnumerator, T&gt;.OfType Method

## Definition

**Namespace:** `Kassyi.ZeroAlloc.Linq`  
**Assembly:** `Kassyi.ZeroAlloc.Linq.dll`

Filters a stack-allocated sequence down to the elements of a given type.

```csharp
public StackEnumerable<OfTypeEnumerator<TEnumerator, T, TResult>, TResult> OfType<TResult>();
```

#### Type Parameters

`TResult`<br>
The type to filter the elements on.

#### Returns

_StackEnumerable&lt;OfTypeEnumerator&lt;TEnumerator, T, TResult&gt;, TResult&gt;_<br>
A sequence over the elements of type `TResult`, in order.

## Remarks

`OfType` is an instance method of [StackEnumerable&lt;TEnumerator, T&gt;](StackEnumerable.md), not an extension in `StackLinqExtensions`. C# infers either all type arguments of a generic method or none, so an extension would need its enumerator and element types spelled out next to `TResult`; as an instance method, a call names only `TResult`: `nodes.AsStackEnumerable().OfType<FieldDeclarationSyntax>()`.

A `null` element is never of type `TResult`, so the filter also drops `null`. After a projection that returns `null` for a miss, `OfType<T>()` narrows a sequence of `T?` to `T`: `Select(x => x as T).OfType<T>()`.

The result is evaluated lazily by an [OfTypeEnumerator](OfTypeEnumerator.md): $O(1)$ auxiliary space and zero heap allocations. `Cast<TResult>()` is still not implemented.

**Execution Target:** Host-only (CPU). A G# kernel reports `GSH0003` for it; device code has no runtime type tests.

## Examples

> [!NOTE]
> Requires assemblies: Kassyi.ZeroAlloc.Linq

```csharp
// Execution Context: Host (CPU)
using System;
using System.Collections.Generic;
using Kassyi.ZeroAlloc.Linq;

object[] values = [1, "one", 2.5, null, "two"];
var words = new List<string>();

values
    .AsStackEnumerable()
    .OfType<string>()
    .AddTo(words);

int whole = values
    .AsStackEnumerable()
    .OfType<int>()
    .Count();

Console.WriteLine($"Words: {string.Join(", ", words)}; whole numbers: {whole}");
```

## See also

### BCL API > LINQ

- [LINQ Architecture and Overview](00_index.md)
- [StackEnumerable&lt;TEnumerator, T&gt; Struct](StackEnumerable.md)
- [OfTypeEnumerator&lt;TEnumerator, TSource, TResult&gt; Struct](OfTypeEnumerator.md)
- [Where Method](Where.md)
