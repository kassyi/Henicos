---
title: "GpuBigIntPrimitiveOps Class (G#)"
site: "G# Documentation"
published: "2026-10-16T00:00:00.000Z"
language: "en"
description: "Reusable zero-allocation GPU device primitives for low-level multi-limb pointer arithmetic, comparisons, and bit manipulations."
---

## GpuBigIntPrimitiveOps Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Provides reusable zero-allocation GPU device primitives for low-level multi-limb arithmetic, comparisons, and bit-level manipulations across custom GPU kernels using unmanaged pointers (`uint*`).

```csharp
[ExcludeFromCodeCoverage]
public static partial class GpuBigIntPrimitiveOps
```

## Methods

| Name | Description |
| --- | --- |
| [DeviceGetBitLength(uint*, int)](#devicegetbitlengthuint-int) | A GPU device function (`[GpuDeviceFunction]`) that computes the significant bit length of a multi-limb integer in device memory. |
| [DeviceExtract64(uint*, int, int)](#deviceextract64uint-int-int) | A GPU device function (`[GpuDeviceFunction]`) that extracts a 64-bit window from a multi-limb integer at the specified bit shift. |
| [DeviceCompareTo(uint*, uint*, int)](#devicecomparetouint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that compares two unsigned multi-limb integers in device memory. |
| [DeviceIsNegative(uint*, int)](#deviceisnegativeuint-int) | A GPU device function (`[GpuDeviceFunction]`) that checks whether the most significant bit is set (negative in two's complement). |
| [DeviceShiftRight1(uint*, int)](#deviceshiftright1uint-int) | A GPU device function (`[GpuDeviceFunction]`) that right-shifts a multi-limb integer by 1 bit in place. |
| [DeviceAdd(uint*, uint*, uint*, int)](#deviceadduint-uint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that adds two multi-limb integers and returns the overflow carry bit. |
| [DeviceSub(uint*, uint*, uint*, int)](#devicesubuint-uint-uint-int) | A GPU device function (`[GpuDeviceFunction]`) that subtracts the right multi-limb integer from the left in device memory. |
| [DeviceIsZero(uint*, int)](#deviceiszerouint-int) | A GPU device function (`[GpuDeviceFunction]`) that determines whether all limbs of an integer are zero. |
| [DeviceIsOne(uint*, int)](#deviceisoneuint-int) | A GPU device function (`[GpuDeviceFunction]`) that determines whether a multi-limb integer equals `1`. |

## DeviceGetBitLength(uint*, int)

Computes the bit length of a multi-limb integer in GPU device memory.

```csharp
[GpuDeviceFunction]
public static unsafe int DeviceGetBitLength(uint* data, int numWords);
```

#### Parameters

`data` _uint*_  
Pointer to the starting limb array in device memory.

`numWords` _int_  
The total number of 32-bit words in the multi-limb integer.

#### Returns

_int_  
The total number of significant bits required to represent the integer, or `0` if all limbs are zero.

## DeviceExtract64(uint*, int, int)

Extracts a 64-bit window from a multi-limb integer in GPU device memory at the specified bit shift.

```csharp
[GpuDeviceFunction]
public static unsafe ulong DeviceExtract64(uint* data, int shift, int numWords);
```

#### Parameters

`data` _uint*_  
Pointer to the starting limb array in device memory.

`shift` _int_  
The bit offset from which to extract the 64-bit value.

`numWords` _int_  
The total number of 32-bit words in the multi-limb integer.

#### Returns

_ulong_  
A 64-bit unsigned integer containing the bits starting at `shift`.

## DeviceCompareTo(uint*, uint*, int)

Compares two multi-limb integers in GPU device memory.

```csharp
[GpuDeviceFunction]
public static unsafe int DeviceCompareTo(uint* a, uint* b, int numWords);
```

#### Parameters

`a` _uint*_  
Pointer to the left limb array in device memory.

`b` _uint*_  
Pointer to the right limb array in device memory.

`numWords` _int_  
The total number of 32-bit words to compare.

#### Returns

_int_  
`1` if `a > b`, `-1` if `a < b`, or `0` if equal.

## DeviceIsNegative(uint*, int)

Checks whether the most significant bit is set (negative representation in two's complement).

```csharp
[GpuDeviceFunction]
public static unsafe bool DeviceIsNegative(uint* data, int numWords);
```

#### Parameters

`data` _uint*_  
Pointer to the starting limb array in device memory.

`numWords` _int_  
The total number of 32-bit words in the multi-limb integer.

#### Returns

_bool_  
`true` if the highest bit of the most significant word is `1`; otherwise, `false`.

## DeviceShiftRight1(uint*, int)

Right-shifts a multi-limb integer by 1 bit in place in GPU device memory.

```csharp
[GpuDeviceFunction]
public static unsafe void DeviceShiftRight1(uint* data, int numWords);
```

#### Parameters

`data` _uint*_  
Pointer to the starting limb array in device memory.

`numWords` _int_  
The total number of 32-bit words in the multi-limb integer.

## DeviceAdd(uint*, uint*, uint*, int)

Adds two multi-limb integers in GPU device memory and returns the final carry bit.

```csharp
[GpuDeviceFunction]
public static unsafe uint DeviceAdd(uint* a, uint* b, uint* dst, int numWords);
```

#### Parameters

`a` _uint*_  
Pointer to the first source limb array in device memory.

`b` _uint*_  
Pointer to the second source limb array in device memory.

`dst` _uint*_  
Pointer to the destination limb array in device memory.

`numWords` _int_  
The total number of 32-bit words to add.

#### Returns

_uint_  
The overflow carry bit (`0` or `1`) generated from adding the highest limbs.

## DeviceSub(uint*, uint*, uint*, int)

Subtracts the right multi-limb integer from the left in GPU device memory.

```csharp
[GpuDeviceFunction]
public static unsafe void DeviceSub(uint* a, uint* b, uint* dst, int numWords);
```

#### Parameters

`a` _uint*_  
Pointer to the minuend limb array in device memory.

`b` _uint*_  
Pointer to the subtrahend limb array in device memory.

`dst` _uint*_  
Pointer to the destination limb array in device memory.

`numWords` _int_  
The total number of 32-bit words to subtract.

## DeviceIsZero(uint*, int)

Determines whether all limbs of an integer in GPU device memory are zero.

```csharp
[GpuDeviceFunction]
public static unsafe bool DeviceIsZero(uint* data, int numWords);
```

#### Parameters

`data` _uint*_  
Pointer to the limb array in device memory.

`numWords` _int_  
The total number of 32-bit words to check.

#### Returns

_bool_  
`true` if all limbs are zero; otherwise, `false`.

## DeviceIsOne(uint*, int)

Determines whether an integer in GPU device memory equals one.

```csharp
[GpuDeviceFunction]
public static unsafe bool DeviceIsOne(uint* data, int numWords);
```

#### Parameters

`data` _uint*_  
Pointer to the limb array in device memory.

`numWords` _int_  
The total number of 32-bit words to check.

#### Returns

_bool_  
`true` if the multi-limb integer evaluates to `1`; otherwise, `false`.

## Examples

### Low-Level Multi-Limb Primitives

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum.Kernels;

unsafe
{
    uint* limbs = stackalloc uint[2] { 0x00000005u, 0x00000001u };
    int bits = GpuBigIntPrimitiveOps.DeviceGetBitLength(limbs, 2);
    bool isZero = GpuBigIntPrimitiveOps.DeviceIsZero(limbs, 2);
    Console.WriteLine($"BitLength={bits}, IsZero={isZero}");
}
```

## Remarks

`GpuBigIntPrimitiveOps` provides pointer-based `[GpuDeviceFunction]` building blocks used by single-thread macro kernels such as `GpuBigIntGcdKernels.ExecuteLehmerGcdMacroKernel` and `GpuBigIntGcdKernels.ExecuteExtendedBinaryGcdMacroKernel`.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuBigIntGcdKernels](GpuBigIntGcdKernels.md)
