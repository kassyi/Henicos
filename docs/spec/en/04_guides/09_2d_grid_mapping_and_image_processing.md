# 2D/3D Grid Mapping and Image Processing Patterns

When programming for GPUs, threads are grouped into Blocks, and Blocks are grouped into Grids. While 1D indexing (`ThreadIndex.X`, `BlockIndex.X`) is sufficient for linear arrays and vectors, many compute-intensive domains—such as image processing, physical simulations, and matrix multiplication (GEMM)—are inherently multi-dimensional.

This guide provides the standard idiomatic patterns for calculating 2D and 3D flat indices, safeguarding boundaries, and launching kernels with multi-dimensional dimensions in G#.

---

## 1. The Multi-Dimensional Challenge

Unlike CPU C#, **G# prohibits the use of multi-dimensional arrays (`T[,]` or `T[,,]`) inside `[GpuKernel]` signatures**. Attempting to use them will result in a **compile error**. This is because GPU memory is fundamentally flat and linear, and passing multi-dimensional object headers would violate the strict zero-allocation memory layout required by CUDA.

```csharp
// Execution Context: G# Kernel (GPU)

// ❌ COMPILE ERROR: Multi-dimensional arrays are prohibited
[GpuKernel]
public static void ProcessImage(float[,] image, int width, int height) { ... }

// ✅ CORRECT: Use a flat 1D array and calculate the flat index manually
[GpuKernel]
public static void ProcessImage(float[] image, int width, int height)
{
    int x = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
    int y = ThreadIndex.Y + BlockIndex.Y * BlockDimension.Y;

    if (x >= width || y >= height) return;

    int flatIndex = y * width + x;
    // Process image[flatIndex]
}
```

Therefore, all device memory is passed and accessed as one-dimensional contiguous arrays (`T[]`), and you must manually calculate the flat 1D index (e.g., `y * width + x`) from the multi-dimensional execution context.

To assist with this, G# provides several built-in structures and macros in the `Kassyi.GSharp.Core` namespace:

- **Execution Context Indices**: `ThreadIndex`, `BlockIndex`, `BlockDimension`, and `GridDimension` to retrieve your 2D/3D hardware coordinates.
- **Gpu Class Macros**: The [Gpu](../03_bcl_api/gpu_class/00_index.md) class (e.g., [Gpu.GridStrideLoop](../03_bcl_api/gpu_class/GridStrideLoop.md)) provides robust patterns for safely traversing flat arrays without manually tracking strides.

For direct 2D and 3D coordinate resolution, the fundamental math relies on `ThreadIndex` and `BlockDimension` as shown below.

## 2. 2D Indexing (Image Processing & Matrices)

### Host (CPU) Launcher Configuration

When launching a 2D kernel, you must configure the grid and block dimensions in both `X` and `Y`.
A standard block size for 2D workloads is `16x16` (256 threads per block, which is a multiple of the 32-thread warp size).

To ensure you launch enough threads to cover images whose dimensions are not perfectly divisible by 16, you must round up the Grid dimensions.

```csharp
// Execution Context: Host (CPU)

int width = 1920;
int height = 1080;
float[] imagePixels = new float[width * height];

// Standard 2D Thread Block (16x16 = 256 threads)
uint blockDimX = 16;
uint blockDimY = 16;

// Round up grid dimensions to cover the entire width/height
uint gridDimX = (uint)((width + blockDimX - 1) / blockDimX);
uint gridDimY = (uint)((height + blockDimY - 1) / blockDimY);

// Launch the kernel via the generated partial class
ImageKernels.LaunchProcessImage(
    imagePixels, width, height,
    gridDimX: gridDimX, gridDimY: gridDimY,
    blockDimX: blockDimX, blockDimY: blockDimY
);
```

### Device (GPU) Kernel Implementation

Inside the kernel, you must:

1. Calculate the global `x` and `y` coordinates using `ThreadIndex`, `BlockIndex`, and `BlockDimension`.
2. Apply a **Boundary Guard** (`if (x >= width || y >= height) return;`) to safely discard the "extra" threads we launched by rounding up the grid.
3. Calculate the flat 1D index `y * width + x`.

```csharp
// Execution Context: G# Kernel (GPU)

using Kassyi.GSharp.Core;

public static partial class ImageKernels
{
    [GpuKernel]
    public static void ProcessImage(float[] pixels, int width, int height)
    {
        // 1. Calculate absolute 2D coordinates
        int x = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
        int y = ThreadIndex.Y + BlockIndex.Y * BlockDimension.Y;

        // 2. Boundary Guard: Exit if the thread falls outside the image bounds
        if (x >= width || y >= height)
        {
            return;
        }

        // 3. Flat Index Calculation
        int flatIndex = y * width + x;

        // Process pixel...
        pixels[flatIndex] *= 2.0f; // Example: brighten image
    }
}
```

> [!WARNING]
> **Never forget the Boundary Guard!** Because `gridDimX` and `gridDimY` are rounded up, threads near the edges of the grid will compute `x` or `y` coordinates that exceed `width` or `height`. Accessing `pixels[flatIndex]` without a boundary guard will cause an Out-Of-Bounds memory violation (CUDA Error 700 / Illegal Memory Access), immediately crashing the kernel.

---

## 3. 3D Indexing (Volumetric Data)

The same principles apply to 3D spaces (e.g., fluid dynamics, voxel rendering).

### Host (CPU) Configuration

A standard 3D block size might be `8x8x8` (512 threads per block).

```csharp
uint blockDimX = 8, blockDimY = 8, blockDimZ = 8;
uint gridDimX = (uint)((width + 7) / 8);
uint gridDimY = (uint)((height + 7) / 8);
uint gridDimZ = (uint)((depth + 7) / 8);

VolumeKernels.LaunchProcessVolume(
    voxelData, width, height, depth,
    gridDimX: gridDimX, gridDimY: gridDimY, gridDimZ: gridDimZ,
    blockDimX: blockDimX, blockDimY: blockDimY, blockDimZ: blockDimZ
);
```

### Device (GPU) Implementation

```csharp
// Execution Context: G# Kernel (GPU)

[GpuKernel]
public static void ProcessVolume(float[] voxels, int width, int height, int depth)
{
    // 1. Calculate absolute 3D coordinates
    int x = ThreadIndex.X + BlockIndex.X * BlockDimension.X;
    int y = ThreadIndex.Y + BlockIndex.Y * BlockDimension.Y;
    int z = ThreadIndex.Z + BlockIndex.Z * BlockDimension.Z;

    // 2. Boundary Guard
    if (x >= width || y >= height || z >= depth) return;

    // 3. Flat Index Calculation (Z * Width * Height + Y * Width + X)
    int flatIndex = (z * height * width) + (y * width) + x;

    voxels[flatIndex] = 1.0f; // Example processing
}
```

## See also

- [BlockDimension](../03_bcl_api/execution_context/BlockDimension.md)
- [ThreadIndex](../03_bcl_api/execution_context/ThreadIndex.md)
- [GridDimension](../03_bcl_api/execution_context/GridDimension.md)
