---
title: "GpuUIntBuilder Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "A shared generic builder for C# 12 Collection Expressions."
---

## GpuUIntBuilder Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.Math.BigInt`  
**Assembly:** `Kassyi.GSharp.Stdlib.Math.dll`

A shared generic builder for C# 12 Collection Expressions.

```csharp
public static class GpuUIntBuilder
```

## Methods

| Name | Description |
| --- | --- |
| [Create&lt;TSize&gt;(ReadOnlySpan&lt;uint&gt;)](#createt-sizereadonlyspan-uint) | Creates a fixed-size GPU unsigned big integer buffer from a span of elements. |

## Create&lt;TSize&gt;(ReadOnlySpan&lt;uint&gt;)

Creates a fixed-size GPU unsigned big integer buffer from a span of elements.

```csharp
public static GpuUIntBigInteger<TSize> Create<TSize>(ReadOnlySpan<uint> elements) where TSize : unmanaged;
```

#### Type Parameters

`TSize`<br>
The unmanaged type defining the length of the fixed buffer.

#### Parameters

`elements` _ReadOnlySpan&lt;uint&gt;_<br>
The elements to copy into the fixed-size buffer.

#### Returns

_GpuUIntBigInteger&lt;TSize&gt;_<br>
A newly allocated fixed-size GPU unsigned big integer buffer.

#### Exceptions

`ArgumentException`<br>
Thrown when the number of elements provided exceeds the capacity of the target fixed-size buffer.

## See also

### BCL API > Math Bignum

- [Standard Math Library (Stdlib.Math)](../00_index.md)
