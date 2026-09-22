---
title: "BitOperations.LeadingZeroCount Method"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Count the number of leading zero bits in an integer."
---

## BitOperations.LeadingZeroCount Method

## Definition

**Namespace:** `System.Numerics`  
**Assembly:** `System.Runtime.dll`

> [!NOTE]
> **Transpilation Marker**
> The `System` assembly references are required for standard C# compilation, IDE intellisense, and CPU fallback execution. However, during G# AOT transpilation to the GPU, this method call acts strictly as a marker. It is completely erased and mapped directly to a native CUDA hardware intrinsic. No actual .NET BCL method invocation, overhead, or dependency exists in the generated C++ device code.


Count the number of leading zero bits in an integer.

## Overloads

| Name | Description |
| --- | --- |
| [LeadingZeroCount(uint)](#leadingzerocount-uint) | Count the number of leading zero bits in an integer. (32-bit) |
| [LeadingZeroCount(ulong)](#leadingzerocount-ulong) | Count the number of leading zero bits in an integer. (64-bit) |

## LeadingZeroCount(uint)

Count the number of leading zero bits in an integer. (32-bit)

```csharp
public static int LeadingZeroCount(uint value);
```

#### Parameters

`value`
`uint`
The 32-bit unsigned integer.

#### Returns

`int`
The number of bits.

## LeadingZeroCount(ulong)

Count the number of leading zero bits in an integer. (64-bit)

```csharp
public static int LeadingZeroCount(ulong value);
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
int bits = BitOperations.LeadingZeroCount(0b_1010_1010_1010_1010u);
```

## Remarks

Maps to specialized CUDA bit manipulation intrinsics (`__popc`, `__clz`, `__ffs`).

## See also

- [Gpu BCL API Overview](../01_api_overview.md)
