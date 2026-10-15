---
title: "GpuBigUInt Struct (G#)"
site: "G# Documentation"
published: "2026-09-24T00:00:00.000Z"
language: "en"
description: "Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory."
---

## GpuBigUInt Struct

## Definition

**Namespace:** `Kassyi.GSharp.Stdlib.GpuMath.BigNum`  
**Assembly:** `Kassyi.GSharp.Stdlib.GpuMath.dll`

Represents an arbitrary-precision unsigned integer stored in NVIDIA GPU global device memory.

```csharp
[StructLayout(LayoutKind.Sequential)]
[GenerateGpuNumberOperators]
public readonly partial struct GpuBigUInt : IDisposable, IGpuBigUIntExpression, IGpuNumber<GpuBigUInt>
```

## Constructors

| Name | Description |
| --- | --- |
| [GpuBigUInt(GpuWorkspace, int)](#gpubiguintgpuworkspace-int) | Allocates uninitialized GPU device memory for an integer of the specified limb length. |
| [GpuBigUInt(GpuWorkspace, uint[])](#gpubiguintgpuworkspace-uint) | Allocates GPU device memory and initializes it with the provided host array in little-endian limb order. |

## GpuBigUInt(GpuWorkspace, int)

Allocates uninitialized GPU device memory for an integer of the specified limb length.

```csharp
public GpuBigUInt(GpuWorkspace workspace, int numWords);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory allocations and kernel dispatch.

`numWords` _int_<br>
The number of 32-bit words to allocate.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `workspace` is `null`.

`ArgumentOutOfRangeException`<br>
Thrown when `numWords` is less than or equal to 0.

## GpuBigUInt(GpuWorkspace, uint[])

Allocates GPU device memory and initializes it with the provided host array in little-endian limb order.

```csharp
public GpuBigUInt(GpuWorkspace workspace, uint[] hostData);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory allocations and kernel dispatch.

`hostData` _uint[]_<br>
An array of 32-bit limb words in little-endian order to copy to device memory.

#### Exceptions

`ArgumentNullException`<br>
Thrown when `workspace` or `hostData` is `null`.

`ArgumentException`<br>
Thrown when `hostData` is empty.

## Properties

| Name | Description |
| --- | --- |
| [DevicePointer](#devicepointer) | Gets the raw 64-bit device memory pointer to the first limb word in GPU global address space. |
| [Handle](#handle) | Gets the underlying GPU memory handle tracking device buffer allocation and lifetime. |
| [IsTemp](#istemp) | Gets a value indicating whether this buffer represents an intermediate temporary buffer managed by expression evaluation. |
| [NumWords](#numwords) | Gets the number of 32-bit unsigned integer limbs (words) allocated for this big integer. |
| [Value](#value) | Gets a deferred expression representing this instance, or evaluates and assigns a deferred expression into this instance in-place. |
| [Workspace](#workspace) | Gets the `GpuWorkspace` associated with this GPU integer instance. |

## DevicePointer

Gets the raw 64-bit device memory pointer to the first limb word in GPU global address space.

```csharp
public IntPtr DevicePointer { get; }
```

## Handle

Gets the underlying GPU memory handle tracking device buffer allocation and lifetime.

```csharp
public GpuBigIntMemoryHandle Handle { get; }
```

## IsTemp

Gets a value indicating whether this buffer represents an intermediate temporary buffer managed by expression evaluation.

```csharp
public bool IsTemp { get; }
```

## NumWords

Gets the number of 32-bit unsigned integer limbs (words) allocated for this big integer.

```csharp
public int NumWords { get; }
```

## Value

Gets a deferred expression representing this instance, or evaluates and assigns a deferred expression into this instance in-place.

```csharp
public GpuBigUIntExpr Value { get; set; }
```

## Workspace

Gets the `GpuWorkspace` associated with this GPU integer instance.

```csharp
public GpuWorkspace Workspace { get; }
```

## Methods

| Name | Description |
| --- | --- |
| [Accept(scoped ref BigIntExpressionVisitor)](#acceptscoped-ref-bigintexpressionvisitor) | Dispatches the visitor to evaluate or process this `GpuBigUInt` node in an expression tree. |
| [Clear(int, int?)](#clearint-int) | Clears (zeros out) a range of limb words in GPU device memory. |
| [ClearWord(int)](#clearwordint) | Clears a single 32-bit limb word at the specified word index in GPU device memory. |
| [CompareTo(GpuBigUInt)](#comparetogpubiguint) | Compares this instance to another `GpuBigUInt` and returns an integer indicating their relative magnitude. |
| [CopyFrom(GpuBigUInt, int, int, int?)](#copyfromgpubiguint-int-int-int) | Copies limb words from another `GpuBigUInt` instance to this instance in GPU device memory. |
| [CreatePowerOfTwo(GpuWorkspace, int, int)](#createpoweroftwogpuworkspace-int-int) | Creates a new `GpuBigUInt` initialized to $2^{\text{bitIndex}}$. |
| [Dispose()](#dispose) | Releases the underlying GPU global device memory handle back to the workspace memory pool. |
| [Equals(GpuBigUInt)](#equalsgpubiguint) | Indicates whether the current instance is equal to another `GpuBigUInt` instance. |
| [Equals(object?)](#equalsobject) | Determines whether the specified object is equal to the current instance. |
| [FromBigInteger(GpuWorkspace, BigInteger, int)](#frombigintegergpuworkspace-biginteger-int) | Creates a new `GpuBigUInt` instance from a .NET standard `BigInteger` value. |
| [GetBitLength()](#getbitlength) | Calculates the minimum number of bits required to represent this big integer without leading zeros. |
| [GetByteCount()](#getbytecount) | Calculates the minimum number of bytes required to represent this big integer. |
| [GetHashCode()](#gethashcode) | Returns the hash code for this instance. |
| [GetIsEven()](#getiseven) | Determines whether the value is an even integer. |
| [GetIsNegative()](#getisnegative) | Determines whether the most significant bit in the highest word is set (used for two's complement sign tracking). |
| [GetTopWords(int)](#gettopwordsint) | Copies the most significant limb words from GPU device memory to a host array. |
| [IsOne()](#isone) | Determines whether the value of this instance is 1 (one). |
| [IsZero()](#iszero) | Determines whether the value of this instance is 0 (zero). |
| [ModInverse(GpuBigUInt)](#modinversegpubiguint) | Computes the modular multiplicative inverse of this instance modulo `modulus`. |
| [PadTo(int)](#padtoint) | Allocates a new `GpuBigUInt` with the specified target word capacity and copies this instance's limbs into it. |
| [SetIsNegative(bool)](#setisnegativebool) | Sets or clears the most significant bit in the highest word (used for two's complement sign tracking). |
| [Square(int)](#squareint) | Computes the square of this number in-place on the GPU. |
| [ToBigInteger(bool)](#tobigintegerbool) | Copies limb data from GPU device memory and reconstructs a .NET standard `BigInteger`. |
| [ToBigIntegerAsync(bool, CancellationToken)](#tobigintegerasyncbool-cancellationtoken) | Asynchronously copies limb data from GPU device memory and reconstructs a .NET standard `BigInteger`, without blocking the calling thread. |
| [ToHostArray()](#tohostarray) | Copies all limb words from GPU device memory to a newly allocated host array. |
| [ToHostArrayAsync(CancellationToken)](#tohostarrayasynccancellationtoken) | Asynchronously copies all limb words from GPU device memory to a newly allocated host array, without blocking the calling thread. |
| [ToProxy()](#toproxy) | Returns a native array proxy struct representing this GPU buffer for P/Invoke AOT context structs. |
| [ToString()](#tostring) | Returns a string representation of the `GpuBigUInt` instance for debugging. |

## Accept(scoped ref BigIntExpressionVisitor)

Dispatches the visitor to evaluate or process this `GpuBigUInt` node in an expression tree.

```csharp
public ScopedBigIntMemory Accept(scoped ref BigIntExpressionVisitor visitor);
```

#### Parameters

`visitor` _BigIntExpressionVisitor_<br>
The expression visitor instance.

#### Returns

_ScopedBigIntMemory_<br>
A `ScopedBigIntMemory` wrapping the evaluated memory handle.

## Clear(int, int?)

Clears (zeros out) a range of limb words in GPU device memory.

```csharp
public void Clear(int offsetWords = 0, int? numWords = null);
```

#### Parameters

`offsetWords` _int_<br>
The word offset within this buffer from which to start clearing.

`numWords` _int?_<br>
The number of words to clear, or `null` to clear until the end of the buffer.

## ClearWord(int)

Clears a single 32-bit limb word at the specified word index in GPU device memory.

```csharp
public void ClearWord(int wordIndex);
```

#### Parameters

`wordIndex` _int_<br>
The 0-based word index to clear.

## CompareTo(GpuBigUInt)

Compares this instance to another `GpuBigUInt` and returns an integer indicating their relative magnitude.

```csharp
public int CompareTo(GpuBigUInt other);
```

#### Parameters

`other` _GpuBigUInt_<br>
The `GpuBigUInt` to compare with this instance.

#### Returns

_int_<br>
-1 if this is less than `other`, 0 if equal, 1 if greater.

## CopyFrom(GpuBigUInt, int, int, int?)

Copies limb words from another `GpuBigUInt` instance to this instance in GPU device memory.

```csharp
public void CopyFrom(GpuBigUInt source, int destOffsetWords = 0, int srcOffsetWords = 0, int? numWords = null);
```

#### Parameters

`source` _GpuBigUInt_<br>
The source `GpuBigUInt` buffer.

`destOffsetWords` _int_<br>
The word offset within this destination buffer.

`srcOffsetWords` _int_<br>
The word offset within the source buffer.

`numWords` _int?_<br>
The number of words to copy, or `null` to copy as many words as fit.

## CreatePowerOfTwo(GpuWorkspace, int, int)

Creates a new `GpuBigUInt` initialized to $2^{\text{bitIndex}}$ ($1 \ll \text{bitIndex}$).

```csharp
public static GpuBigUInt CreatePowerOfTwo(GpuWorkspace workspace, int numWords, int bitIndex);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory and device copy.

`numWords` _int_<br>
The total word capacity of the resulting big integer.

`bitIndex` _int_<br>
The 0-based bit position to set to 1.

#### Returns

_GpuBigUInt_<br>
A newly allocated `GpuBigUInt` representing $2^{\text{bitIndex}}$.

## Dispose()

Releases the underlying GPU global device memory handle back to the workspace memory pool.

```csharp
public void Dispose();
```

## Equals(GpuBigUInt)

Indicates whether the current instance is equal to another `GpuBigUInt` instance.

```csharp
public bool Equals(GpuBigUInt other);
```

#### Parameters

`other` _GpuBigUInt_<br>
A `GpuBigUInt` to compare with this instance.

#### Returns

_bool_<br>
`true` if the current instance is equal to the `other` parameter; otherwise, `false`.

## Equals(object?)

Determines whether the specified object is equal to the current instance.

```csharp
public override bool Equals(object? obj);
```

#### Parameters

`obj` _object?_<br>
The object to compare with the current instance.

#### Returns

_bool_<br>
`true` if `obj` is a `GpuBigUInt` and equals the current instance; otherwise, `false`.

## FromBigInteger(GpuWorkspace, BigInteger, int)

Creates a new `GpuBigUInt` instance from a .NET standard `BigInteger` value.

```csharp
public static GpuBigUInt FromBigInteger(GpuWorkspace workspace, BigInteger val, int minWords = 0);
```

#### Parameters

`workspace` _GpuWorkspace_<br>
The GPU workspace managing memory allocations.

`val` _BigInteger_<br>
The `BigInteger` value to convert.

`minWords` _int_<br>
The minimum word capacity to allocate on the GPU.

#### Returns

_GpuBigUInt_<br>
A newly allocated `GpuBigUInt` containing the magnitude of `val`.

## GetBitLength()

Calculates the minimum number of bits required to represent this big integer without leading zeros.

```csharp
public int GetBitLength();
```

#### Returns

_int_<br>
The bit length of the number, or 0 if the value is zero.

## GetByteCount()

Calculates the minimum number of bytes required to represent this big integer.

```csharp
public int GetByteCount();
```

#### Returns

_int_<br>
The byte length of the number, or 1 if the value is zero.

## GetHashCode()

Returns the hash code for this instance.

```csharp
public override int GetHashCode();
```

#### Returns

_int_<br>
A 32-bit signed integer hash code.

## GetIsEven()

Determines whether the value is an even integer.

```csharp
public bool GetIsEven();
```

#### Returns

_bool_<br>
`true` if the least significant bit is 0; otherwise, `false`.

## GetIsNegative()

Determines whether the most significant bit in the highest word is set (used for two's complement sign tracking).

```csharp
public bool GetIsNegative();
```

#### Returns

_bool_<br>
`true` if the MSB of the highest word is set; otherwise, `false`.

## GetTopWords(int)

Copies the most significant limb words from GPU device memory to a host array.

```csharp
public uint[] GetTopWords(int wordCount);
```

#### Parameters

`wordCount` _int_<br>
The number of top words to copy.

#### Returns

_uint[]_<br>
A host array containing the most significant limb words.

## IsOne()

Determines whether the value of this instance is 1 (one).

```csharp
public bool IsOne();
```

#### Returns

_bool_<br>
`true` if the value equals 1; otherwise, `false`.

## IsZero()

Determines whether the value of this instance is 0 (zero).

```csharp
public bool IsZero();
```

#### Returns

_bool_<br>
`true` if all limb words equal 0; otherwise, `false`.

## ModInverse(GpuBigUInt)

Computes the modular multiplicative inverse of this instance modulo `modulus`.

```csharp
public GpuBigUInt ModInverse(GpuBigUInt modulus);
```

#### Parameters

`modulus` _GpuBigUInt_<br>
The modulus operand.

#### Returns

_GpuBigUInt_<br>
A new `GpuBigUInt` containing the modular inverse ($x \equiv \text{this}^{-1} \pmod{\text{modulus}}$).

## PadTo(int)

Allocates a new `GpuBigUInt` with the specified target word capacity and copies this instance's limbs into it.

```csharp
public GpuBigUInt PadTo(int targetWords);
```

#### Parameters

`targetWords` _int_<br>
The target word capacity of the new buffer.

#### Returns

_GpuBigUInt_<br>
A new `GpuBigUInt` padded or truncated to `targetWords`.

## SetIsNegative(bool)

Sets or clears the most significant bit in the highest word (used for two's complement sign tracking).

```csharp
public void SetIsNegative(bool value);
```

#### Parameters

`value` _bool_<br>
`true` to set the sign bit; `false` to clear it.

## Square(int)

Computes the square of this number in-place on the GPU.

```csharp
public bool Square(int logicalWords);
```

#### Parameters

`logicalWords` _int_<br>
The logical number of limb words in the input operand.

#### Returns

_bool_<br>
`true` if the square operation succeeded; otherwise, `false`.

#### Exceptions

`InvalidOperationException`<br>
Thrown when `NumWords` is less than `logicalWords * 2`.

## ToBigInteger(bool)

Copies limb data from GPU device memory and reconstructs a .NET standard `BigInteger`.

```csharp
public BigInteger ToBigInteger(bool isNegative = false);
```

#### Parameters

`isNegative` _bool_<br>
`true` to return a negative `BigInteger`; otherwise, `false`.

#### Returns

_BigInteger_<br>
A .NET `BigInteger` representing the value stored in GPU memory.

## ToBigIntegerAsync(bool, CancellationToken)

Asynchronously copies limb data from GPU device memory and reconstructs a .NET standard `BigInteger`, without blocking the calling thread while work queued on the workspace stream completes.

```csharp
public async ValueTask<BigInteger> ToBigIntegerAsync(bool isNegative = false, CancellationToken cancellationToken = default);
```

#### Parameters

`isNegative` _bool_<br>
`true` to return a negative `BigInteger`; otherwise, `false`.

`cancellationToken` _CancellationToken_<br>
A token that stops waiting; queued device work is not canceled.

#### Returns

_ValueTask&lt;BigInteger&gt;_<br>
A .NET `BigInteger` representing the value stored in GPU memory.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled.

> [!NOTE]
> The copy is ordered on the workspace stream, so it observes all work enqueued before the call. It is staged through a pooled page-locked buffer (see `CudaHostTransfer`), and a canceled call never writes to caller-visible memory.

## ToHostArray()

Copies all limb words from GPU device memory to a newly allocated host array.

```csharp
public uint[] ToHostArray();
```

#### Returns

_uint[]_<br>
An array of 32-bit unsigned integers representing the limbs in little-endian order.

## ToHostArrayAsync(CancellationToken)

Asynchronously copies all limb words from GPU device memory to a newly allocated host array, without blocking the calling thread.

```csharp
public async ValueTask<uint[]> ToHostArrayAsync(CancellationToken cancellationToken = default);
```

#### Parameters

`cancellationToken` _CancellationToken_<br>
A token that stops waiting; queued device work is not canceled.

#### Returns

_ValueTask&lt;uint[]&gt;_<br>
An array of 32-bit unsigned integers representing the limbs in little-endian order.

#### Exceptions

`OperationCanceledException`<br>
`cancellationToken` was canceled.

## ToProxy()

Returns a native array proxy struct representing this GPU buffer for P/Invoke AOT context structs.

```csharp
public GSharpArrayProxy ToProxy();
```

#### Returns

_GSharpArrayProxy_<br>
A native array proxy struct.

## ToString()

Returns a string representation of the `GpuBigUInt` instance for debugging.

```csharp
public override string ToString();
```

#### Returns

_string_<br>
A string containing word count and device pointer address.

## Operators

| Name | Description |
| --- | --- |
| `Addition(GpuBigUInt, GpuBigUInt)` | Adds two big integers using lazy expression evaluation. |
| `Subtraction(GpuBigUInt, GpuBigUInt)` | Subtracts one big integer from another using lazy expression evaluation. |
| `Increment(GpuBigUInt)` | Increments the big integer by 1 in-place on the GPU. |
| `Decrement(GpuBigUInt)` | Decrements the big integer by 1 in-place on the GPU. |
| `Multiply(GpuBigUInt, GpuBigUInt)` | Multiplies two big integers using lazy expression evaluation. |
| `Division(GpuBigUInt, GpuBigUInt)` | Divides one big integer by another using lazy expression evaluation. |
| `Modulus(GpuBigUInt, GpuBigUInt)` | Computes the remainder of dividing one big integer by another using lazy expression evaluation. |
| `BitwiseAnd(GpuBigUInt, GpuBigUInt)` | Performs bitwise AND on two big integers using lazy expression evaluation. |
| `BitwiseOr(GpuBigUInt, GpuBigUInt)` | Performs bitwise OR on two big integers using lazy expression evaluation. |
| `ExclusiveOr(GpuBigUInt, GpuBigUInt)` | Performs bitwise exclusive OR on two big integers using lazy expression evaluation. |
| `LeftShift(GpuBigUInt, int)` | Shifts a big integer left by a specified number of bits using lazy expression evaluation. |
| `RightShift(GpuBigUInt, int)` | Shifts a big integer right by a specified number of bits using lazy expression evaluation. |
| `OnesComplement(GpuBigUInt)` | Performs bitwise NOT on a big integer using lazy expression evaluation. |
| `Equality(GpuBigUInt, GpuBigUInt)` | Determines whether two big integers represent the same value on the GPU. |
| `Inequality(GpuBigUInt, GpuBigUInt)` | Determines whether two big integers represent different values on the GPU. |
| `LessThan(GpuBigUInt, GpuBigUInt)` | Determines whether the first big integer is less than the second on the GPU. |
| `GreaterThan(GpuBigUInt, GpuBigUInt)` | Determines whether the first big integer is greater than the second on the GPU. |
| `LessThanOrEqual(GpuBigUInt, GpuBigUInt)` | Determines whether the first big integer is less than or equal to the second on the GPU. |
| `GreaterThanOrEqual(GpuBigUInt, GpuBigUInt)` | Determines whether the first big integer is greater than or equal to the second on the GPU. |
| `Implicit(GpuBigUInt to GSharpArrayProxy)` | Implicitly converts this `GpuBigUInt` to `GSharpArrayProxy`. |

## Examples

### Lazy Expression Evaluation (CPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Stdlib.GpuMath

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

Console.WriteLine($"Result={result.ToBigInteger(false)}");
```

### Immediate Evaluation (GPU)

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core, Kassyi.GSharp.Stdlib.GpuMath

```csharp
using System;
using Kassyi.GSharp.Core;
using Kassyi.GSharp.Stdlib.GpuMath.BigNum;

Example.ComputeMath();

public class Example
{
    // Execution Context: G# Kernel (GPU)
    [GpuKernel]
    public static void ComputeMath()
    {
        // On the GPU device, fixed-size stack types provide immediate zero-allocation evaluation.
        GpuUInt128 sigA = [150];
        GpuUInt128 sigB = [50];
        GpuUInt128 sigC = [25];

        GpuUInt128 resultSig = (sigA + sigB) - sigC;
        Console.WriteLine($"Kernel result word[0]={resultSig.Value.Limbs[0]}");
    }
}
```

## Remarks

All arithmetic and bitwise operations are executed on the GPU using SIMD/warp-level intrinsics.

**Memory Management:** Implements `IDisposable` to return allocated GPU device memory to the `GpuWorkspace` memory pool.

## See also

### BCL API > Math (BigNum)

- [Standard Math Library (Stdlib.Math)](../../../03_bcl_api/math_bignum/00_index.md)
- [Arbitrary-Precision Integers (GpuBigInt)](../../../03_bcl_api/math_bignum/gpu_big_int_and_uint/00_index.md)
- [GpuWorkspace](../../../03_bcl_api/math_bignum/gpu_workspace/GpuWorkspace.md)

### Guides

- [4. Practical Guides and Best Practices](../../../04_guides/00_index.md)
- [7. Stdlib Math and Lazy Evaluation](../../../04_guides/07_stdlib_math_and_lazy_evaluation.md)
- [4. BigNum Expression Trees Guide](../../../04_guides/04_bignum_expression_trees_guide.md)
