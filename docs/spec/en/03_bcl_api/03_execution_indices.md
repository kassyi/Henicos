# 3. Execution Indices Reference

> [!NOTE]
> **Conceptual Model: The Execution Hierarchy (CPU vs. GPU)**
>
> In standard C# (CPU) programming, you typically dispatch tasks to a `ThreadPool`, where each thread dynamically picks up the next available item in a queue. You rarely care about the absolute ID of the thread.
> 
> GPU execution is fundamentally different. A GPU `[GpuKernel]` is launched across a rigid, massive 3D coordinate system (the **Grid**). The Grid is subdivided into smaller 3D groups called **Thread Blocks**. 
> When the kernel runs, tens of thousands of threads execute the exact same function simultaneously. To prevent them from processing the exact same data, every thread must use its unique spatial coordinates (`ThreadIndex` and `BlockIndex`) to mathematically calculate which piece of memory it is responsible for. This is called *Self-Identification*.

G# kernels are executed across this massive array of threads organized into a Grid of Thread Blocks. The `Kassyi.GSharp.Core` namespace provides static classes that allow each thread to query its unique coordinates within this execution topology.

These static classes map directly to NVIDIA CUDA built-in variables (`threadIdx`, `blockIdx`, `blockDim`, `gridDim`).

> [!TIP]
> **GPU Macro Support vs. Manual Index Calculation**
> For standard linear array processing, G# provides high-level transpiler macros such as [`Gpu.GridStrideLoop(...)`](02_gpu_class.md#26-transpiler-macros) that automatically calculate global thread offsets, stride across large buffers, and perform boundary safety checks.
> 
> The execution index primitives described in this document (`ThreadIndex`, `BlockIndex`, `BlockDimension`, `GridDimension`) are provided so that you can manually implement custom coordinate math, multi-dimensional spatial layouts, stencil neighbor fetching, or cooperative shared-memory staging whenever explicit topology control is required.

---

## 3.0 The 2×2 Execution Matrix: Why Are There Four Classes?

At first glance, all four static classes expose identical C# signatures (`public static int X { get; }`, `Y`, `Z`). However, they represent two completely orthogonal dimensions of execution information:

### The Matrix

| Scope \ Property | **Coordinate (Position)**<br>*(“Where am I?”)* | **Dimension (Capacity)**<br>*(“How big is the group?”)* |
| :--- | :--- | :--- |
| **Local Block Scope**<br>*(Inside a single Thread Block)* | **`ThreadIndex`** (`threadIdx`)<br>• Local coordinate of this specific thread.<br>• **Varies** for every thread in the block.<br>• Value: `0` to `BlockDimension - 1`. | **`BlockDimension`** (`blockDim`)<br>• Total thread capacity of the block.<br>• **Constant** for all threads in the kernel.<br>• Configured by host `block` launch parameter. |
| **Global Grid Scope**<br>*(Across the entire GPU Grid)* | **`BlockIndex`** (`blockIdx`)<br>• Coordinate of this block within the grid.<br>• **Varies** between different blocks.<br>• Value: `0` to `GridDimension - 1`. | **`GridDimension`** (`gridDim`)<br>• Total block capacity of the grid.<br>• **Constant** for all threads in the kernel.<br>• Configured by host `grid` launch parameter. |

### The Office Building Analogy
If you are coming from CPU development, imagine a large corporate campus:
- **`GridDimension`**: The total number of rooms in the building (e.g., `100` rooms).
- **`BlockIndex`**: The room number you are currently sitting in (e.g., Room `3`).
- **`BlockDimension`**: The number of desks inside each room (e.g., `256` desks per room).
- **`ThreadIndex`**: Your individual desk number inside your room (e.g., Desk `5`).

To calculate your **Global Employee ID** (the unique linear index across the entire company):
$$\text{Global ID} = (\text{Room Number} \times \text{Desks Per Room}) + \text{Desk Number}$$
$$\text{globalId} = (\text{BlockIndex.X} \times \text{BlockDimension.X}) + \text{ThreadIndex.X} = (3 \times 256) + 5 = 773$$

### Host Launch Relationship
When the host CPU launches a kernel, it supplies the **Dimensions**. The GPU hardware then dynamically assigns the **Indices** to each executing thread:

```csharp
// Host-side CPU dispatch:
kernel.Launch(
    grid: new Dim3(100, 1, 1),   // -> Configures GridDimension (X=100)
    block: new Dim3(256, 1, 1),  // -> Configures BlockDimension (X=256)
    buffer
);

// Device-side GPU execution:
// GridDimension.X  is fixed at 100 for all threads.
// BlockDimension.X is fixed at 256 for all threads.
// BlockIndex.X     is assigned by hardware (0 to 99).
// ThreadIndex.X    is assigned by hardware (0 to 255).
```

---

## 3.1 `ThreadIndex`

Represents the calling thread's **relative position inside its thread block** (maps to CUDA `threadIdx`).

#### Syntax
```csharp
public static class ThreadIndex
{
    public static int X { get; }
    public static int Y { get; }
    public static int Z { get; }
}
```

- **Nature:** Dynamic (differs for every thread inside the same block).
- **Value Range:** `0` to `BlockDimension.{X|Y|Z} - 1`.
- **Primary Uses:**
  - Identifying thread roles inside a warp or block.
  - Offsetting into Block-Shared Memory (`GpuShared<T>`).
  - Thread 0 election within a block (`if (ThreadIndex.X == 0)`).

---

## 3.2 `BlockIndex`

Represents the **calling block's position inside the grid** (maps to CUDA `blockIdx`).

#### Syntax
```csharp
public static class BlockIndex
{
    public static int X { get; }
    public static int Y { get; }
    public static int Z { get; }
}
```

- **Nature:** Dynamic per block (identical for all 256+ threads inside the same block, but varies between blocks).
- **Value Range:** `0` to `GridDimension.{X|Y|Z} - 1`.
- **Primary Uses:**
  - Partitioning large data arrays across independent streaming multiprocessors (SMs).
  - Determining which tile/chunk of data the current block is responsible for.

---

## 3.3 `BlockDimension`

Represents the **number of threads allocated per thread block** (maps to CUDA `blockDim`).

#### Syntax
```csharp
public static class BlockDimension
{
    public static int X { get; }
    public static int Y { get; }
    public static int Z { get; }
}
```

- **Nature:** Uniform Constant (identical across all threads and blocks for a given kernel launch).
- **Value:** Equal to the `block` dimension argument specified at kernel launch (typically up to 1024 threads total).
- **Primary Uses:**
  - Used as the stride multiplier in global coordinate calculations (`BlockIndex.X * BlockDimension.X`).
  - Loop stepping in block-stride loops.

---

## 3.4 `GridDimension`

Represents the **total number of thread blocks dispatched in the grid** (maps to CUDA `gridDim`).

#### Syntax
```csharp
public static class GridDimension
{
    public static int X { get; }
    public static int Y { get; }
    public static int Z { get; }
}
```

- **Nature:** Uniform Constant (identical across all threads and blocks for a given kernel launch).
- **Value:** Equal to the `grid` dimension argument specified at kernel launch.
- **Primary Uses:**
  - Calculating the total stride of the entire GPU grid (`GridDimension.X * BlockDimension.X`) for grid-stride loops.
  - Multi-grid boundary partitioning.

---

## 3.5 Common Indexing Patterns

To manipulate linear arrays in memory, multi-dimensional execution coordinates must usually be flattened into a single, globally unique 1D index. 

G# provides built-in GPU macros ([`Gpu.GridStrideLoop`](02_gpu_class.md#26-transpiler-macros)) that automatically generate stride loops and bounds checks. However, manually computing coordinates via `ThreadIndex` and `BlockIndex` remains supported and encouraged whenever explicit hardware control, custom multi-dimensional mappings, or shared memory staging is desired.

### Comparison: High-Level Macro vs. Manual Indexing

| Pattern | High-Level Macro ([`Gpu.GridStrideLoop`](02_gpu_class.md#26-transpiler-macros)) | Manual Indexing (`BlockIndex` / `ThreadIndex`) |
| :--- | :--- | :--- |
| **Code Verbosity** | Single-line closure (`i => ...`) | Manual arithmetic and `if` guard |
| **Bounds Checking** | Handled automatically by the transpiled loop | Explicit `if (globalId < data.Length)` required |
| **Grid Sizing** | Grid-stride loop handles arbitrary buffer lengths safely | Kernel launch grid must cover buffer or use manual stride |
| **Flexibility** | Optimized for flat linear streaming | Full control over 2D/3D shapes, warps, and tile staging |

```csharp
// Approach A: High-Level GPU Macro
[GpuKernel]
public static void ProcessWithMacro(float[] data)
{
    // The transpiler expands this into an optimal CUDA grid-stride loop
    Gpu.GridStrideLoop(data.Length, i =>
    {
        data[i] *= 2.0f;
    });
}

// Approach B: Manual Coordinate Indexing
[GpuKernel]
public static void ProcessWithManualIndex(float[] data)
{
    int globalId = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
    if (globalId < data.Length)
    {
        data[globalId] *= 2.0f;
    }
}
```

### Pattern 1: 1D Global Indexing (Manual)
The standard manual pattern for processing flat 1D arrays (e.g., vectors, raw buffers).

```csharp
[GpuKernel]
public static void Process1DArray(float[] data)
{
    // Global Thread ID = (Block Index * Threads Per Block) + Thread Index
    int globalId = BlockIndex.X * BlockDimension.X + ThreadIndex.X;

    // Boundary check is critical because the grid size might exceed the array length
    if (globalId < data.Length)
    {
        data[globalId] *= 2.0f;
    }
}
```

### Pattern 2: 2D Image/Matrix Indexing

Used when processing 2D structures like images, surfaces, or matrices mapped to a 2D Thread Block and Grid launch.

> [!NOTE]
> **C# Parallel: Flat 1D Buffers vs. Jagged Arrays (`T[][]`)**
> 
> In high-performance C# on the CPU, developers often replace jagged arrays (`float[][]`) with flat single-dimensional arrays (`float[]`) indexed via `y * width + x` to avoid pointer indirection and improve CPU cache locality.
> 
> On the GPU, this flattening pattern is **mandatory**, not optional:
> 1. **No Jagged Arrays (`T[][]`)**: Nested/jagged arrays are arrays of reference heap pointers (`object[]` / pointer to array). The GPU's zero-allocation model and hardware execution engine strictly prohibit managed reference pointers and dynamic heap dereferencing.
> 2. **Contiguous Memory Requirement**: GPU memory controllers require a single contiguous linear allocation so that warp instructions can achieve maximum coalesced memory bandwidth.
> 
> Thus, 2D coordinates `(x, y)` computed from execution indices are always mapped to linear memory using standard row-major indexing: `index = y * width + x`.

```csharp
[GpuKernel]
public static void Process2DMatrix(float[] matrix, int width, int height)
{
    // Calculate global 2D coordinates across the Grid
    int x = BlockIndex.X * BlockDimension.X + ThreadIndex.X;
    int y = BlockIndex.Y * BlockDimension.Y + ThreadIndex.Y;

    if (x < width && y < height)
    {
        // Flatten the 2D coordinate into a 1D linear memory address (y * width + x)
        int index = y * width + x;
        matrix[index] *= 2.0f;
    }
}
```

> [!TIP]
> Memory access in CUDA is fastest when adjacent threads in a warp access adjacent memory addresses (Memory Coalescing). Because the `X` dimension represents the contiguous inner loop of thread execution, you should always map your data's contiguous memory dimension to the `X` dimension of the thread block.
