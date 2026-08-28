namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents a 3-dimensional coordinate vector used for grid and block dimensions in GPU kernels.
/// </summary>
/// <remarks>
/// This struct is a marker intercepted by the GSharp Transpiler and mapped to CUDA <c>dim3</c>.
/// </remarks>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("IDE", "IDE0051", Justification = "Public API exposed for library consumers")]
public struct Dim3
{
    /// <summary>Gets the X component of the 3D vector.</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public int X => GpuOnlyException.Throw<int>();

    /// <summary>Gets the Y component of the 3D vector.</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public int Y => GpuOnlyException.Throw<int>();

    /// <summary>Gets the Z component of the 3D vector.</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public int Z => GpuOnlyException.Throw<int>();
}

/// <summary>
/// Provides access to the current GPU thread coordinates (<c>threadIdx</c>) within the thread block.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[CudaIntrinsicPattern("threadIdx.{0}")]
public static class ThreadIndex
{
    /// <summary>Gets the thread index along the X axis (<c>threadIdx.x</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int X => GpuOnlyException.Throw<int>();

    /// <summary>Gets the thread index along the Y axis (<c>threadIdx.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Y => GpuOnlyException.Throw<int>();

    /// <summary>Gets the thread index along the Z axis (<c>threadIdx.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Z => GpuOnlyException.Throw<int>();
}

/// <summary>
/// Provides access to the current thread block coordinates (<c>blockIdx</c>) within the grid.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[CudaIntrinsicPattern("blockIdx.{0}")]
public static class BlockIndex
{
    /// <summary>Gets the block index along the X axis (<c>blockIdx.x</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int X => GpuOnlyException.Throw<int>();

    /// <summary>Gets the block index along the Y axis (<c>blockIdx.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Y => GpuOnlyException.Throw<int>();

    /// <summary>Gets the block index along the Z axis (<c>blockIdx.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Z => GpuOnlyException.Throw<int>();
}

/// <summary>
/// Provides access to the dimensions of the thread block (<c>blockDim</c>).
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[CudaIntrinsicPattern("blockDim.{0}")]
public static class BlockDimension
{
    /// <summary>Gets the number of threads per block along the X axis (<c>blockDim.x</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int X => GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of threads per block along the Y axis (<c>blockDim.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Y => GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of threads per block along the Z axis (<c>blockDim.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Z => GpuOnlyException.Throw<int>();
}

/// <summary>
/// Provides access to the dimensions of the grid (<c>gridDim</c>).
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[CudaIntrinsicPattern("gridDim.{0}")]
public static class GridDimension
{
    /// <summary>Gets the number of blocks per grid along the X axis (<c>gridDim.x</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int X => GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of blocks per grid along the Y axis (<c>gridDim.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Y => GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of blocks per grid along the Z axis (<c>gridDim.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Z => GpuOnlyException.Throw<int>();
}

/// <summary>
/// Provides hardware intrinsics, synchronization primitives, atomic operations, and macro markers for GPU kernels.
/// </summary>
[SuppressMessage("ReSharper", "UnusedType.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("ReSharper", "UnusedMember.Global", Justification = "Public API exposed for GPU kernels")]
[SuppressMessage("IDE", "IDE0051", Justification = "Public API exposed for library consumers")]
public static class Gpu
{
    /// <summary>
    /// Synchronizes all threads in the current block barrier (<c>__syncthreads()</c>).
    /// </summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    [CudaIntrinsic("__syncthreads")]
    public static void SyncThreads() => GpuOnlyException.Throw();

    /// <summary>
    /// Enforces warp shuffle operation using CUDA <c>__shfl_sync</c> across active threads in the warp.
    /// </summary>
    /// <param name="val">The value to shuffle.</param>
    /// <param name="srcLane">The source lane index (0-31).</param>
    /// <param name="mask">The warp thread participation mask (default: <c>0xffffffff</c>).</param>
    /// <returns>The shuffled value from the target lane.</returns>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    [CudaIntrinsic("__shfl_sync")]
    public static int WarpShuffle(int val, int srcLane, uint mask = 0xffffffff) => GpuOnlyException.Throw<int>();

    /// <summary>
    /// Enforces warp shuffle operation using CUDA <c>__shfl_sync</c> across active threads in the warp.
    /// </summary>
    /// <param name="val">The value to shuffle.</param>
    /// <param name="srcLane">The source lane index (0-31).</param>
    /// <param name="mask">The warp thread participation mask (default: <c>0xffffffff</c>).</param>
    /// <returns>The shuffled value from the target lane.</returns>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    [CudaIntrinsic("__shfl_sync")]
    public static float WarpShuffle(float val, int srcLane, uint mask = 0xffffffff) => GpuOnlyException.Throw<float>();

    /// <summary>
    /// Synchronizes all threads across the entire cooperative grid (<c>cg::this_grid().sync()</c>).
    /// </summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    [CudaIntrinsic("cg::this_grid().sync")]
    public static void SyncGrid() => GpuOnlyException.Throw();

    /// <summary>
    /// Marker method for JIT Constant Folding. Replaced with the literal macro value during runtime compilation.
    /// </summary>
    /// <param name="macroName">The name of the JIT macro parameter.</param>
    /// <returns>A typed placeholder evaluated at GPU compile-time.</returns>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int JitConstant(string macroName) => GpuOnlyException.Throw<int>();

    /// <summary>Atomically adds an integer value to the target address and returns the old value.</summary>
    [CudaIntrinsic("atomicAdd")]
    public static int AtomicAdd(ref int address, int val) => GpuOnlyException.Throw<int>();

    /// <summary>Atomically adds an unsigned integer value to the target address and returns the old value.</summary>
    [CudaIntrinsic("atomicAdd")]
    public static uint AtomicAdd(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Atomically adds an unsigned 64-bit integer to the target address and returns the old value.</summary>
    [CudaIntrinsic("atomicAdd")]
    public static ulong AtomicAdd(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();

    /// <summary>Atomically adds a floating-point value to the target address and returns the old value.</summary>
    [CudaIntrinsic("atomicAdd")]
    public static float AtomicAdd(ref float address, float val) => GpuOnlyException.Throw<float>();

    /// <summary>Atomically subtracts an integer value from the target address and returns the old value.</summary>
    [CudaIntrinsic("atomicSub")]
    public static int AtomicSub(ref int address, int val) => GpuOnlyException.Throw<int>();

    /// <summary>Atomically subtracts an unsigned integer value from the target address and returns the old value.</summary>
    [CudaIntrinsic("atomicSub")]
    public static uint AtomicSub(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Adds two unsigned 32-bit integers with carry-in and carry-out propagation.</summary>
    [CudaIntrinsic("__gsharp_add_cc")]
    public static uint AddWithCarry(uint a, uint b, ref ulong carry) => GpuOnlyException.Throw<uint>();

    /// <summary>Subtracts two unsigned 32-bit integers with borrow-in and borrow-out propagation.</summary>
    [CudaIntrinsic("__gsharp_sub_cc")]
    public static uint SubWithBorrow(uint a, uint b, ref long borrow) => GpuOnlyException.Throw<uint>();

    /// <summary>Atomically computes bitwise OR on the target unsigned 32-bit integer address.</summary>
    [CudaIntrinsic("atomicOr")]
    public static uint AtomicOr(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Atomically computes bitwise OR on the target unsigned 64-bit integer address.</summary>
    [CudaIntrinsic("atomicOr")]
    public static ulong AtomicOr(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();

    /// <summary>Atomically computes the maximum value between the address content and the supplied value.</summary>
    [CudaIntrinsic("atomicMax")]
    public static uint AtomicMax(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Atomically computes the maximum value between the address content and the supplied value (64-bit).</summary>
    [CudaIntrinsic("atomicMax")]
    public static ulong AtomicMax(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();

    /// <summary>Reverses the bit order of a 32-bit unsigned integer using the <c>__brev</c> hardware instruction.</summary>
    [CudaIntrinsic("__brev")]
    public static uint BitReverse(uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Computes the full 64-bit product of two 32-bit unsigned integers without overflow.</summary>
    [CudaIntrinsic("__gsharp_mul_wide")]
    public static ulong MultiplyWide(uint a, uint b) => GpuOnlyException.Throw<ulong>();

    /// <summary>Computes the high 64 bits of the product of two 64-bit unsigned integers (<c>__umul64hi</c>).</summary>
    [CudaIntrinsic("__umul64hi")]
    public static ulong MultiplyHigh(ulong a, ulong b) => GpuOnlyException.Throw<ulong>();

    /// <summary>
    /// Injects raw inline PTX assembly or CUDA C++ instructions directly into the transpiled output.
    /// </summary>
    /// <param name="ptx">The raw PTX or C++ string to inject.</param>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static void InlinePtx(string ptx) => GpuOnlyException.Throw();

    /// <summary>
    /// Transpilation macro that unrolls a loop body at compile-time across the specified range.
    /// </summary>
    /// <param name="start">The constant loop starting index.</param>
    /// <param name="count">The constant loop iteration count.</param>
    /// <param name="body">The loop body delegate.</param>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static void ForUnroll(int start, int count, Action<int> body) => GpuOnlyException.Throw();

    /// <summary>Delegate for compile-time unrolled loops with a passed execution context.</summary>
    public delegate void UnrollAction<TContext>(ref TContext ctx, int index);

    /// <summary>
    /// Transpilation macro that unrolls a loop body at compile-time with context state propagation.
    /// </summary>
    public static void ForUnroll<TContext>(ref TContext ctx, int start, int count, UnrollAction<TContext> body) => GpuOnlyException.Throw();

    /// <summary>
    /// Generates a standard CUDA Grid-Stride Loop over the specified bound across all threads and blocks.
    /// </summary>
    /// <param name="maxBounds">The total element count to process.</param>
    /// <param name="body">The loop body delegate.</param>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static void GridStrideLoop(int maxBounds, Action<int> body) => GpuOnlyException.Throw();

    /// <summary>Delegate for grid-stride loops with a passed execution context.</summary>
    public delegate void GridStrideAction<TContext>(ref TContext ctx, int index);

    /// <summary>
    /// Generates a standard CUDA Grid-Stride Loop with context state propagation.
    /// </summary>
    public static void GridStrideLoop<TContext>(ref TContext ctx, int maxBounds, GridStrideAction<TContext> body) => GpuOnlyException.Throw();

    /// <summary>
    /// Ensures that the enclosed action is executed exclusively by thread 0 of block 0 (<c>threadIdx.x == 0 &amp;&amp; blockIdx.x == 0</c>).
    /// </summary>
    /// <param name="body">The action to execute on a single thread.</param>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static void SingleThread(Action body) => GpuOnlyException.Throw();
}
