---
title: "BitOperations.PopCount Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Returns the population count (number of bits set) of a mask."
---

## BitOperations.PopCount Method

## Definition

**Namespace:** `System.Numerics`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Returns the population count (number of bits set) of a mask.

## Overloads

| Name | Description |
| --- | --- |
| [PopCount(uint)](#popcount-uint) | Returns the population count (number of bits set) of a mask. (32-bit) |
| [PopCount(ulong)](#popcount-ulong) | Returns the population count (number of bits set) of a mask. (64-bit) |

## PopCount(uint)

Returns the population count (number of bits set) of a mask. (32-bit)

```csharp
public static int PopCount(uint value);
```

#### Parameters

`value`
`uint`
The 32-bit unsigned integer.

#### Returns

`int`
The number of bits.

## PopCount(ulong)

Returns the population count (number of bits set) of a mask. (64-bit)

```csharp
public static int PopCount(ulong value);
```

#### Parameters

`value`
`ulong`
The 64-bit unsigned integer.

#### Returns

`int`
The number of bits.

## Examples

### Bit Manipulation

```csharp
int bits = BitOperations.PopCount(0b_1010_1010_1010_1010u);
```

## Remarks

Maps to specialized CUDA bit manipulation intrinsics (`__popc`, `__clz`, `__ffs`).

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
