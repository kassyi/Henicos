---
title: "Math and BitOperations"
site: "G# Documentation"
published: "2026-09-22T00:00:00.000Z"
language: "en"
description: "Standard math functions, fast math approximations, and hardware bit operations."
---

# Math and BitOperations (Primitives)

Standard math functions, fast math approximations, and hardware bit operations.

> [!NOTE]
> **Device & Host Execution**
> Because these are standard C# BCL methods (like `System.Math.Sin`), they can naturally be executed on the Host (CPU). GSharp's transpiler extends this capability, allowing them to also be executed seamlessly inside GPU kernels (Device).

## In This Section

- [Math_Abs](Math_Abs.md): Returns the absolute value of a specified number.
- [Math_Min](Math_Min.md): Returns the smaller of two specified numbers.
- [Math_Max](Math_Max.md): Returns the larger of two specified numbers.
- [Math_Exp](Math_Exp.md): Returns e raised to the specified power.
- [Math_Log](Math_Log.md): Returns the natural logarithm of a specified number.
- [Math_Sqrt](Math_Sqrt.md): Returns the square root of a specified number.

- [Math_Sin](Math_Sin.md): Returns the sine of the specified angle.
- [Math_Cos](Math_Cos.md): Returns the cosine of the specified angle.
- [Math_Tan](Math_Tan.md): Returns the tangent of the specified angle.
- [FastMath_Exp](FastMath_Exp.md): Returns e raised to the specified power using hardware fast-math.
- [FastMath_Log](FastMath_Log.md): Returns the natural logarithm using hardware fast-math.
- [FastMath_Sqrt](FastMath_Sqrt.md): Returns the square root using hardware fast-math.
- [BitOperations_PopCount](BitOperations_PopCount.md): Returns the population count of a mask.
- [BitOperations_LeadingZeroCount](BitOperations_LeadingZeroCount.md): Count the number of leading zero bits.
- [BitOperations_TrailingZeroCount](BitOperations_TrailingZeroCount.md): Count the number of trailing zero bits.
- [VectorTypes](VectorTypes.md): SIMD vector types like float3, int4.

## See also

- [Gpu BCL API Overview](../00_overview.md)
- [Math.Sin](Math_Sin.md)
- [Math.Cos](Math_Cos.md)
- [Math.Log](Math_Log.md)
- [Math.Exp](Math_Exp.md)
- [FastMath.Sqrt](FastMath_Sqrt.md)
- [VectorTypes](VectorTypes.md)
