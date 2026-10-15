---
title: "GpuBigUInt Class (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory."
---

## GpuBigUInt Class

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.

```csharp
public class GpuBigUInt : IDisposable, IGpuBigUIntExpression
```

## Constructors

| Name                                                                       | Description                                                                                              |
| -------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------- |
| [GpuBigUInt(GpuWorkspace, int)](#gpuuintbigintegergpuworkspace-int)     | Allocates uninitialized GPU device memory for an integer of the specified limb length.                   |
| [GpuBigUInt(GpuWorkspace, uint[])](#gpuuintbigintegergpuworkspace-uint) | Allocates GPU device memory and initializes it with the provided host array in little-endian limb order. |

## GpuBigUInt(GpuWorkspace, int)

Allocates uninitialized GPU device memory for an integer of the specified limb length.

```csharp
public GpuBigUInt(GpuWorkspace workspace, int numWords);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The parent GPU execution workspace.

`numWords` _int_<br>
The number of 32-bit limbs to allocate.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `workspace` is `null`.

`OutOfMemoryException`<br>
Thrown when device memory allocation fails.

`CudaException`<br>
Thrown by the default backend when CUDA driver memory allocation fails.

## GpuBigUInt(GpuWorkspace, uint[])

Allocates GPU device memory and initializes it with the provided host array in little-endian limb order.

```csharp
public GpuBigUInt(GpuWorkspace workspace, uint[] hostData);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The parent GPU execution workspace.

`hostData` _uint[]_<br>
The host data array containing 32-bit limbs in little-endian order.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `workspace` or `hostData` is `null`.

`OutOfMemoryException`<br>
Thrown when device memory allocation fails.

`CudaException`<br>
Thrown when CUDA driver memory allocation or host-to-device memory copy fails.

## Properties

| Name                            | Description                                                                        |
| ------------------------------- | ---------------------------------------------------------------------------------- |
| [Workspace](#workspace)             | Gets the parent `GpuWorkspace` associated with this instance.                        |
| [DevicePointer](#devicepointer) | Gets the native GPU global device memory pointer holding the integer limb data.    |
| [IsEven](#iseven)               | Gets a value indicating whether this big integer is an even number.                |
| [IsNegative](#isnegative)       | Gets or sets the sign bit of this integer (stored in the MSB of the highest word). |
| [NumWords](#numwords)           | Gets the total number of 32-bit words (limbs) allocated for this big integer.      |
| [Value](#value)                 | Gets or sets the lazy expression wrapper assigned to this big integer buffer.      |

## Workspace

Gets the parent `GpuWorkspace` associated with this instance.

```csharp
public GpuWorkspace Context { get; }
```

## DevicePointer

Gets the native GPU global device memory pointer (`CUdeviceptr`) holding the integer limb data.

```csharp
public IntPtr DevicePointer { get; }
```

## IsEven

Gets a value indicating whether this big integer is an even number.

```csharp
public bool IsEven { get; }
```

## IsNegative

Gets or sets the sign bit of this integer (stored in the MSB of the highest word).

```csharp
public bool IsNegative { get; set; }
```

## NumWords

Gets the total number of 32-bit words (limbs) allocated for this big integer.

```csharp
public int NumWords { get; }
```

## Value

Gets or sets the lazy expression wrapper assigned to this big integer buffer.

```csharp
public GpuBigUIntExpr Value { get; set; }
```

## Methods

| Name                                                                                    | Description                                                                                          |
| --------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| [Clear(int, int?)](#clearint-int)                                                       | Clears the GPU memory (sets to 0) for the specified range.                                           |
| [ClearWord(int)](#clearwordint)                                                         | Clears the single word at the specified index to zero on the GPU.                                    |
| [CompareTo(GpuBigUInt)](#comparetogpuuintbiginteger)                             | Compares this big integer with another `GpuBigUInt` buffer.                                   |
| [CopyFrom(GpuBigUInt, int, int, int?)](#copyfromgpuuintbiginteger-int-int-int)   | Copies words from a source `GpuBigUInt` to this buffer on the GPU device.                     |
| [CreatePowerOfTwo(GpuWorkspace, int, int)](#createpoweroftwogpuworkspace-int-int)           | Creates a new `GpuBigUInt` on the GPU representing `2^bitIndex`.                              |
| [Dispose()](#dispose)                                                                   | Releases the allocated GPU device memory or returns the buffer to the workspace memory pool if pooled. |
| [Equals(object?)](#equalsobject)                                                        | Determines whether this instance and a specified object have equivalent numerical value.             |
| [Evaluate(GpuWorkspace, GpuBigUInt?)](#evaluategpuworkspace-gpuuintbiginteger)       | Evaluates the underlying GPU expression tree into the target buffer.                                 |
| [FromBigInteger(GpuWorkspace, BigInteger, int)](#frombigintegergpuworkspace-biginteger-int) | Creates a `GpuBigUInt` by uploading a BCL `BigInteger` to the GPU.                            |
| [GetBitLength()](#getbitlength)                                                         | Computes the total number of significant bits used to represent this integer on the GPU.             |
| [GetByteCount()](#getbytecount)                                                         | Computes the total number of bytes required to hold this integer.                                    |
| [GetHashCode()](#gethashcode)                                                           | Returns the hash code for this instance based on its GPU device memory address.                      |
| [GetTopWords(int)](#gettopwordsint)                                                     | Copies the most significant limbs (top words) from GPU memory to host.                               |
| [IsOne()](#isone)                                                                       | Checks whether this big integer equals one on the GPU.                                               |
| [IsZero()](#iszero)                                                                     | Checks whether this big integer equals zero on the GPU.                                              |
| [PadTo(int)](#padtoint)                                                                 | Creates a new `GpuBigUInt` padded to the specified target words with zeros.                   |
| [Square(int)](#squareint)                                                               | Computes self-squaring (`A * A`) in-place on the GPU.                                                |
| [ToBigInteger(bool)](#tobigintegerbool)                                                 | Downloads the integer from the GPU and converts it to a BCL `BigInteger`.                            |
| [ToBigIntegerAsync(bool, CancellationToken)](#tobigintegerasyncbool-cancellationtoken) | Like `ToBigInteger`, but waits for the GPU without blocking the calling thread.                      |
| [ToHostArray()](#tohostarray)                                                           | Copies the limb data from GPU device memory back to a host managed array.                            |
| [ToHostArrayAsync(CancellationToken)](#tohostarrayasynccancellationtoken)               | Like `ToHostArray`, but waits for the GPU without blocking the calling thread.                       |
| [ToString()](#tostring)                                                                 | Returns a diagnostic string describing this GPU big integer buffer.                                  |

## Clear(int, int?)

Clears the GPU memory (sets to 0) for the specified range.

```csharp
public void Clear(int offsetWords = 0, int? numWords = null);
```

#### Parameters

`offsetWords` _int_<br>
The starting word offset.

`numWords` _int?_<br>
The number of words to clear. If null, clears to the end.

## ClearWord(int)

Clears the single word at the specified index to zero on the GPU.

```csharp
public void ClearWord(int wordIndex);
```

#### Parameters

`wordIndex` _int_<br>
The zero-based limb index.

## CompareTo(GpuBigUInt)

Compares this big integer with another `GpuBigUInt` buffer.

```csharp
public int CompareTo(GpuBigUInt other);
```

#### Parameters

`other` _GpuUIntBigInteger_<br>
The target big integer to compare against.

#### Returns

_int_<br>
-1 if this is less than `other`, 0 if equal, 1 if greater.

## CopyFrom(GpuBigUInt, int, int, int?)

Copies words from a source `GpuBigUInt` to this buffer on the GPU device.

```csharp
public void CopyFrom(GpuBigUInt source, int destOffsetWords = 0, int srcOffsetWords = 0, int? numWords = null);
```

#### Parameters

`source` _GpuUIntBigInteger_<br>
The source GPU buffer.

`destOffsetWords` _int_<br>
The starting word offset in this destination buffer.

`srcOffsetWords` _int_<br>
The starting word offset in the source buffer.

`numWords` _int?_<br>
The number of 32-bit limbs to copy, or `null` to copy maximum available.

## CreatePowerOfTwo(GpuWorkspace, int, int)

Creates a new `GpuBigUInt` on the GPU representing `2^bitIndex`.

```csharp
public static GpuBigUInt CreatePowerOfTwo(GpuWorkspace workspace, int numWords, int bitIndex);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`numWords` _int_<br>
The allocated word count.

`bitIndex` _int_<br>
The zero-based bit index to set.

#### Returns

_GpuUIntBigInteger_<br>
A new `GpuBigUInt` holding the power of two value.

## Dispose()

Releases the allocated GPU device memory or returns the buffer to the workspace memory pool if pooled.

```csharp
public void Dispose();
```

## Equals(object?)

Determines whether this instance and a specified object have equivalent numerical value.

```csharp
public override bool Equals(object? obj);
```

#### Parameters

`obj` _object?_<br>
The object to compare with the current object.

#### Returns

_bool_<br>
`true` if the specified object is equal to the current object; otherwise, `false`.

## Evaluate(GpuWorkspace, GpuBigUInt?)

Evaluates the underlying GPU expression tree into the target buffer.

```csharp
public GpuBigUInt Evaluate(GpuWorkspace workspace, GpuBigUInt? targetResult = null);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU execution workspace.

`targetResult` _GpuUIntBigInteger?_<br>
Optional destination buffer. If `null`, returns this instance.

#### Returns

_GpuUIntBigInteger_<br>
The evaluated `GpuBigUInt` result buffer.

## FromBigInteger(GpuWorkspace, BigInteger, int)

Creates a `GpuBigUInt` by uploading a BCL `BigInteger` to the GPU.

```csharp
public static GpuBigUInt FromBigInteger(GpuWorkspace workspace, BigInteger val, int minWords = 0);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace.

`val` _BigInteger_<br>
The BCL `BigInteger` to upload.

`minWords` _int_<br>
The minimum number of words to allocate.

#### Returns

_GpuUIntBigInteger_<br>
A new `GpuBigUInt` containing the value on the GPU.

## GetBitLength()

Computes the total number of significant bits used to represent this integer on the GPU.

```csharp
public int GetBitLength();
```

#### Returns

_int_<br>
The total number of significant bits.

## GetByteCount()

Computes the total number of bytes required to hold this integer.

```csharp
public int GetByteCount();
```

#### Returns

_int_<br>
The total number of bytes.

## GetHashCode()

Returns the hash code for this instance based on its GPU device memory address.

```csharp
public override int GetHashCode();
```

#### Returns

_int_<br>
A hash code for the current object.

## GetTopWords(int)

Copies the most significant limbs (top words) from GPU memory to host.

```csharp
public uint[] GetTopWords(int wordCount);
```

#### Parameters

`wordCount` _int_<br>
The number of top words to retrieve.

#### Returns

_uint[]_<br>
A host array containing the most significant limbs.

## IsOne()

Checks whether this big integer equals one on the GPU.

```csharp
public bool IsOne();
```

#### Returns

_bool_<br>
`true` if the value is one; otherwise, `false`.

## IsZero()

Checks whether this big integer equals zero on the GPU.

```csharp
public bool IsZero();
```

#### Returns

_bool_<br>
`true` if the value is zero; otherwise, `false`.

## PadTo(int)

Creates a new `GpuBigUInt` padded to the specified target words with zeros.

```csharp
public GpuBigUInt PadTo(int targetWords);
```

#### Parameters

`targetWords` _int_<br>
The requested minimum number of words.

#### Returns

_GpuUIntBigInteger_<br>
A new `GpuBigUInt` instance padded to the target size.

## Square(int)

Computes self-squaring (`A * A`) in-place on the GPU.

```csharp
public bool Square(int logicalWords);
```

#### Parameters

`logicalWords` _int_<br>
The logical number of input limbs to square.

#### Returns

_bool_<br>
`true` if executed successfully; otherwise, `false`.

## ToBigInteger(bool)

Downloads the integer from the GPU and converts it to a BCL `BigInteger`.

```csharp
public BigInteger ToBigInteger(bool isNegative = false);
```

#### Parameters

`isNegative` _bool_<br>
Whether the resulting `BigInteger` should be negative.

#### Returns

_BigInteger_<br>
A BCL `BigInteger` representation.

## ToBigIntegerAsync(bool, CancellationToken)

Asynchronously downloads the integer from the GPU and converts it to a BCL `BigInteger`, without blocking the calling thread while work queued on the workspace stream completes.

```csharp
public async ValueTask<BigInteger> ToBigIntegerAsync(bool isNegative = false, CancellationToken cancellationToken = default);
```

#### Parameters

`isNegative` _bool_<br>
Whether the resulting `BigInteger` should be negative.

`cancellationToken` _CancellationToken_<br>
A token that stops waiting. Queued device work is not canceled.

#### Returns

_ValueTask&lt;BigInteger&gt;_<br>
A BCL `BigInteger` representation.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled.

> [!NOTE]
> The copy is ordered on the workspace stream, so it observes all work enqueued before the call. It is staged through a pooled page-locked buffer (see `CudaHostTransfer`), and a canceled call never writes to caller-visible memory.

## ToHostArray()

Copies the limb data from GPU device memory back to a host managed array.

```csharp
public uint[] ToHostArray();
```

#### Returns

_uint[]_<br>
A 32-bit unsigned integer array of length `NumWords` in little-endian limb order.

## ToHostArrayAsync(CancellationToken)

Asynchronously copies the limb data from GPU device memory back to a newly allocated host array, without blocking the calling thread.

```csharp
public async ValueTask<uint[]> ToHostArrayAsync(CancellationToken cancellationToken = default);
```

#### Parameters

`cancellationToken` _CancellationToken_<br>
A token that stops waiting. Queued device work is not canceled.

#### Returns

_ValueTask&lt;uint[]&gt;_<br>
A 32-bit unsigned integer array of length `NumWords` in little-endian limb order.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled.

## ToString()

Returns a diagnostic string describing this GPU big integer buffer.

```csharp
public override string ToString();
```

#### Returns

_string_<br>
A diagnostic string.

## Operators

| Name                                                       | Description                                                                                     |
| ---------------------------------------------------------- | ----------------------------------------------------------------------------------------------- |
| `Addition(GpuBigUInt, GpuBigUInt)`           | Adds two big integers using lazy expression evaluation.                                         |
| `Subtraction(GpuBigUInt, GpuBigUInt)`        | Subtracts one big integer from another using lazy expression evaluation.                        |
| `Increment(GpuBigUInt)`                             | Increments the big integer by 1 in-place on the GPU.                                            |
| `Decrement(GpuBigUInt)`                             | Decrements the big integer by 1 in-place on the GPU.                                            |
| `Multiply(GpuBigUInt, GpuBigUInt)`           | Multiplies two big integers using lazy expression evaluation.                                   |
| `Division(GpuBigUInt, GpuBigUInt)`           | Divides one big integer by another using lazy expression evaluation.                            |
| `Modulus(GpuBigUInt, GpuBigUInt)`            | Computes the remainder of dividing one big integer by another using lazy expression evaluation. |
| `BitwiseAnd(GpuBigUInt, GpuBigUInt)`         | Performs bitwise AND on two big integers using lazy expression evaluation.                      |
| `BitwiseOr(GpuBigUInt, GpuBigUInt)`          | Performs bitwise OR on two big integers using lazy expression evaluation.                       |
| `ExclusiveOr(GpuBigUInt, GpuBigUInt)`        | Performs bitwise exclusive OR on two big integers using lazy expression evaluation.             |
| `LeftShift(GpuBigUInt, int)`                        | Shifts a big integer left by a specified number of bits using lazy expression evaluation.       |
| `RightShift(GpuBigUInt, int)`                       | Shifts a big integer right by a specified number of bits using lazy expression evaluation.      |
| `OnesComplement(GpuBigUInt)`                        | Performs bitwise NOT on a big integer using lazy expression evaluation.                         |
| `Equality(GpuBigUInt, GpuBigUInt)`           | Determines whether two big integers represent the same value on the GPU.                        |
| `Inequality(GpuBigUInt, GpuBigUInt)`         | Determines whether two big integers represent different values on the GPU.                      |
| `LessThan(GpuBigUInt, GpuBigUInt)`           | Determines whether the first big integer is less than the second on the GPU.                    |
| `GreaterThan(GpuBigUInt, GpuBigUInt)`        | Determines whether the first big integer is greater than the second on the GPU.                 |
| `LessThanOrEqual(GpuBigUInt, GpuBigUInt)`    | Determines whether the first big integer is less than or equal to the second on the GPU.        |
| `GreaterThanOrEqual(GpuBigUInt, GpuBigUInt)` | Determines whether the first big integer is greater than or equal to the second on the GPU.     |

## Examples

### Lazy Expression Evaluation (CPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Runtime, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using System.Numerics;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

// Execution Context: Host (CPU)
using var workspace = new GpuWorkspace(1024, GpuExecutionEngine.GSharpAot);

using var a = GpuBigUInt.FromBigInteger(workspace, new BigInteger(150), minWords: 4);
using var b = GpuBigUInt.FromBigInteger(workspace, new BigInteger(50), minWords: 4);
using var c = GpuBigUInt.FromBigInteger(workspace, new BigInteger(25), minWords: 4);
using var result = new GpuBigUInt(workspace, 8);

// Construct lazy expression tree: (a + b) * c
result.Value = (a + b) * c;

Console.WriteLine($"Result={(result).ToBigInteger(false)}");
```

### Immediate Evaluation (GPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void ComputeMath()
    {
        // On the GPU device, dynamic AST creation is prohibited.
        // We use GpuBigIntMath for immediate zero-allocation evaluation.
        
        GpuUInt128 sigA = [150];
        GpuUInt128 sigB = [50];
        GpuUInt128 sigC = [25];
        GpuUInt128 tempSum = [0];
        GpuUInt128 resultSig = [0];
        
        GpuBigIntMath.Add(sigA, sigB, tempSum);
        GpuBigIntMath.Multiply(tempSum, sigC, resultSig);
    }
}
```

## Remarks

All arithmetic and bitwise operations are executed on the GPU using SIMD/warp-level intrinsics.

**Memory Management:** Implements `IDisposable` to ensure deterministic GPU memory deallocation.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
