---
title: "Gpu.HintUnroll Method (G#)"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Hints the compiler to unroll the immediately following loop."
---

## Gpu.HintUnroll Method

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Hints the compiler to unroll the immediately following loop.

## Overloads

| Name | Description |
| --- | --- |
| [HintUnroll()](#hintunroll) | Hints the compiler to unroll the immediately following loop completely. |
| [HintUnroll(int)](#hintunroll-int) | Hints the compiler to unroll the immediately following loop by a specific factor. |

## HintUnroll()

Hints the compiler to unroll the immediately following loop completely.

```csharp
public static void HintUnroll();
```

## HintUnroll(int)

Hints the compiler to unroll the immediately following loop by a specific factor.

```csharp
public static void HintUnroll(int factor);
```

#### Parameters

`factor`
`int`
The unroll factor.

## Examples

### Loop Unrolling

```csharp
// Completely unroll the loop (assuming fixed bounds)
Gpu.HintUnroll();
for (int i = 0; i < 4; i++)
{
    // ...
}

// Unroll by a factor of 2
Gpu.HintUnroll(2);
for (int i = 0; i < count; i++)
{
    // ...
}
```

## Remarks

Maps to `#pragma unroll` or `#pragma unroll N`. Must be placed immediately before a `for` or `while` loop.

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
