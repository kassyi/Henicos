---
title: "Wmma Class and Warp Matrix Types (G#)"
site: "G# Documentation"
published: "2026-11-11T00:00:00.000Z"
language: "en"
description: "Tensor Core warp matrix multiply-accumulate (WMMA): the shape and layout markers, the fragments, and the Wmma operations."
---

## Wmma Class and Warp Matrix Types

## Definition

**Namespace:** `Kassyi.GSharp.Core`  
**Assembly:** `Kassyi.GSharp.Core.dll`

Gives a kernel the Tensor Cores of the GPU: one warp (32 threads) computes the product of two small matrix tiles and adds
a third, `D = A * B + C`, in one operation. The operations map to CUDA's `nvcuda::wmma` API.

```csharp
public static class Wmma
```

## The Tile: M, N and K

A WMMA operation works on tiles of a fixed size, chosen by a shape type. A shape is named after three sizes, in the
order `M`, `N`, `K`:

- `M`: the number of rows of A, C and D.
- `N`: the number of columns of B, C and D.
- `K`: the inner size that the product sums over: the number of columns of A and of rows of B.

So A is an `M x K` tile, B is `K x N`, and C and D are `M x N`. The order is that of the template arguments of CUDA's
`nvcuda::wmma::fragment`, which NVIDIA writes as `m16n16k16`.

## Shapes

| Shape | A | B | C and D | Input type (A, B) | Accumulator type (C, D) | GPU |
| --- | --- | --- | --- | --- | --- | --- |
| `WmmaM16N16K16` | 16 x 16 | 16 x 16 | 16 x 16 | `sbyte` or `byte` | `int` | SM 7.2 or later |
| `WmmaM32N8K16` | 32 x 16 | 16 x 8 | 32 x 8 | `sbyte` or `byte` | `int` | SM 7.2 or later |
| `WmmaM8N32K16` | 8 x 16 | 16 x 32 | 8 x 32 | `sbyte` or `byte` | `int` | SM 7.2 or later |
| `WmmaM8N8K4` | 8 x 4 | 4 x 8 | 8 x 8 | `double` | `double` | SM 8.0 or later |

Every shape implements `IWmmaShape`. Another combination of shape and element type is reported as
[GSH0076](diagnostics/GSH0076.md).

## Layouts

A and B tiles are stored in memory in one of two orders, chosen by a layout type that implements `IWmmaLayout`:

| Layout | CUDA | Meaning |
| --- | --- | --- |
| `WmmaRowMajor` | `nvcuda::wmma::row_major` | The elements of a row are next to each other in memory. |
| `WmmaColumnMajor` | `nvcuda::wmma::col_major` | The elements of a column are next to each other in memory. |

The accumulator does not carry a layout in its type; `Wmma.Load` and `Wmma.Store` take it as a `WmmaMemoryLayout` value
(`WmmaMemoryLayout.RowMajor` or `WmmaMemoryLayout.ColumnMajor`).

Shapes and layouts are markers: they hold no data and are used only as type arguments. Declaring one as a local, a
parameter or a field is reported as [GSH0076](diagnostics/GSH0076.md). Only the shapes and layouts of
`Kassyi.GSharp.Core` are markers. A struct of your own that implements `IWmmaShape` or `IWmmaLayout` is an ordinary
struct: it cannot choose a shape or a layout, and it compiles like any other struct.

## Fragments

A fragment holds one tile, spread over the registers of the 32 threads of the warp. Which thread holds which element is
decided by the hardware and is not visible to the program; a fragment is read and written only through `Wmma`.

| Fragment | Holds | CUDA |
| --- | --- | --- |
| `WmmaMatrixA<TShape, T, TLayout>` | A (`M x K`) | `nvcuda::wmma::fragment<matrix_a, ...>` |
| `WmmaMatrixB<TShape, T, TLayout>` | B (`K x N`) | `nvcuda::wmma::fragment<matrix_b, ...>` |
| `WmmaAccumulator<TShape, T>` | C or D (`M x N`) | `nvcuda::wmma::fragment<accumulator, ...>` |

A fragment can be declared as a local of a kernel or a device function, and passed to a `[GpuDeviceFunction]` by
`ref`, `ref readonly` or `in`. It cannot be a kernel parameter, a field, an array element, the target of a pointer or a
return value ([GSH0076](diagnostics/GSH0076.md)).

## Methods

| Name | Description |
| --- | --- |
| `Wmma.Fill` | Sets every element of a fragment (A, B or accumulator) to one value. |
| `Wmma.Load` | Loads a tile from global or shared memory into a fragment. |
| `Wmma.Store` | Stores an accumulator fragment to global or shared memory. |
| `Wmma.MultiplyAccumulate` | Computes `d = a * b + c` on fragments. |

```csharp
public static void Fill<TShape, T, TLayout>(ref WmmaMatrixA<TShape, T, TLayout> fragment, T value);
public static void Fill<TShape, T, TLayout>(ref WmmaMatrixB<TShape, T, TLayout> fragment, T value);
public static void Fill<TShape, T>(ref WmmaAccumulator<TShape, T> fragment, T value);

public static void Load<TShape, T, TLayout>(ref WmmaMatrixA<TShape, T, TLayout> fragment, ref readonly T source,
    uint leadingDimension);
public static void Load<TShape, T, TLayout>(ref WmmaMatrixB<TShape, T, TLayout> fragment, ref readonly T source,
    uint leadingDimension);
public static void Load<TShape, T>(ref WmmaAccumulator<TShape, T> fragment, ref readonly T source,
    uint leadingDimension, WmmaMemoryLayout layout);

public static void Store<TShape, T>(ref T destination, ref readonly WmmaAccumulator<TShape, T> fragment,
    uint leadingDimension, WmmaMemoryLayout layout);

public static void MultiplyAccumulate<TShape, TIn, TLayoutA, TLayoutB, TAcc>(ref WmmaAccumulator<TShape, TAcc> d,
    ref readonly WmmaMatrixA<TShape, TIn, TLayoutA> a, ref readonly WmmaMatrixB<TShape, TIn, TLayoutB> b,
    ref readonly WmmaAccumulator<TShape, TAcc> c);
```

#### Parameters

`source`, `destination`<br>
The first element of the tile, an element of global memory (an array or span parameter) or of shared memory
(`SharedMemory<T>`), aligned to 256 bits. A local variable or a temporary value is reported as
[GSH0076](diagnostics/GSH0076.md).

`leadingDimension` _uint_<br>
The distance in elements from one row to the next (row-major) or from one column to the next (column-major) in memory.
It must be a multiple of 16 bytes: 16 for `sbyte` and `byte`, 4 for `int`, 2 for `double`.

`layout` _WmmaMemoryLayout_<br>
The order in which the accumulator tile is stored in memory.

#### Exceptions

[GpuOnlyException](01_diagnostics_and_exceptions.md#gpuonlyexception)<br>
Thrown when a method is called on the host CPU outside a kernel.

## Examples

### One Tile on the Host

> [!NOTE]
> Requires assemblies: Kassyi.GSharp.Core

```csharp
using System;
using Kassyi.GSharp.Core;

// Execution Context: G# Kernel (GPU)

try {
    WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA = default;
    WmmaAccumulator<WmmaM16N16K16, int> acc = default;

    // Fragments live in the registers of a warp; every Wmma operation needs the GPU
    Wmma.Fill(ref fragA, (sbyte)1);
    Wmma.Fill(ref acc, 0);
    Console.WriteLine("Fragments filled.");
} catch (GpuOnlyException) {
    Console.WriteLine("Wmma operations can only be executed on the GPU.");
}
```

### Multiplying a 16 x 16 Tile in a Kernel

The kernel multiplies a 16 x 16 tile of `sbyte` values by another and adds a third tile of `int` values. All three are
stored row by row, so the leading dimension is 16. Launch it with one warp.

```csharp
using Kassyi.GSharp.Core;

public static partial class TileKernels
{
    [GpuKernel]
    public static void MultiplyTileKernel(sbyte[] a, sbyte[] b, int[] c, int[] d)
    {
        WmmaMatrixA<WmmaM16N16K16, sbyte, WmmaRowMajor> fragA = default;
        WmmaMatrixB<WmmaM16N16K16, sbyte, WmmaRowMajor> fragB = default;
        WmmaAccumulator<WmmaM16N16K16, int> acc = default;

        Wmma.Load(ref fragA, in a[0], 16);
        Wmma.Load(ref fragB, in b[0], 16);
        Wmma.Load(ref acc, in c[0], 16, WmmaMemoryLayout.RowMajor);
        Wmma.MultiplyAccumulate(ref acc, in fragA, in fragB, in acc);
        Wmma.Store(ref d[0], in acc, 16, WmmaMemoryLayout.RowMajor);
    }
}
```

The compiler generates `TileKernels.LaunchMultiplyTileKernel` for the host:

```csharp
sbyte[] a = new sbyte[16 * 16];
sbyte[] b = new sbyte[16 * 16];
int[] c = new int[16 * 16];
int[] d = new int[16 * 16];

// d[i * 16 + j] = c[i * 16 + j] + the sum over k of a[i * 16 + k] * b[k * 16 + j]
TileKernels.LaunchMultiplyTileKernel(a, b, c, d, blockDimX: 32);
```

## Remarks

- **The whole warp runs each operation.** Every `Wmma` call must be reached by all 32 threads of the warp with the same
  arguments (the same addresses, leading dimension and layout). A call in a branch that only some threads of the warp
  take has no defined result on CUDA.
- **CUDA only.** The native CPU build (the StandardCpp dialect) has no Tensor Cores: a kernel that uses `Wmma` compiles,
  but each `Wmma` operation throws `NotSupportedException` when it runs, so its launch fails.
- The `in` and `ref` arguments of `Wmma.Load` and `Wmma.Store` name the first element of the tile in memory; the
  operation reads or writes the whole tile from there.

## See also

### BCL API

- [BCL API and Hardware Intrinsics Reference](00_index.md)
- [GSH0076: Unsupported warp matrix (WMMA) type or operation](diagnostics/GSH0076.md)
- [SharedMemory&lt;T&gt;](memory/SharedMemory.md)
