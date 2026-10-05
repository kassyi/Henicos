namespace Kassyi.GSharp.Core;

/// <summary>
/// Represents a 3-dimensional coordinate vector used for grid and block dimensions in GPU kernels.
/// </summary>
/// <remarks>
/// This struct is a marker intercepted by the GSharp Transpiler and mapped to CUDA <c>dim3</c>.
/// </remarks>
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
#pragma warning disable IDE0051 // Justification: Public API exposed for library consumers
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
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
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
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
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
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
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
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
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
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
#pragma warning disable IDE0051 // Justification: Public API exposed for library consumers
public static class Gpu
{
    /// <summary>
    /// Provides compiler optimization hints for kernel code generation.
    /// </summary>
    public static class Hint
    {
        /// <summary>
        /// Instructs the GSharp Transpiler to emit a <c>#pragma unroll</c> directive immediately preceding the target loop.
        /// </summary>
        /// <param name="factor">The maximum number of iterations to unroll. A value of 0 indicates that the compiler should determine the optimal unroll factor heuristically.</param>
        /// <remarks>
        /// <para><b>Usage Scenario:</b></para>
        /// <para>This method is highly recommended for optimizing pure computational loops (such as SGEMM or array traversals) to improve Instruction-Level Parallelism (ILP). It relies on the NVCC compiler to unroll the loop during the final backend compilation phase.</para>
        /// <para><b>Comparison with <see cref="Gpu.ForUnroll(int, int, System.Action{int})"/>:</b></para>
        /// <para>Unlike <c>ForUnroll</c>, which literally duplicates loop bodies in the intermediate C++ code (useful for generating compile-time constants), <c>Hint.Unroll</c> does not bloat the source code and prevents compiler crashes on large iteration counts. Furthermore, because it is a simple marker method, it guarantees zero closure allocations when executing the kernel purely on the C# CPU host for testing.</para>
        /// </remarks>
        public static void Unroll(int factor = 0)
        {
        }
    }

    /// <summary>
    /// Synchronizes all threads in the current block barrier (<c>__syncthreads()</c>).
    /// </summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    [CudaIntrinsic("__syncthreads")]
    public static void SyncThreads() => GpuOnlyException.Throw();

    /// <summary>
    /// Synchronizes all threads across the entire cooperative grid (<c>cg::this_grid().sync()</c>).
    /// </summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    [CudaIntrinsic("cg::this_grid().sync")]
    public static void SyncGrid() => GpuOnlyException.Throw();

    /// <summary>
    /// Executes the provided action, automatically inserting a grid synchronization (<c>cg::this_grid().sync()</c>)
    /// between each statement in the action body.
    /// </summary>
    /// <param name="body">The action containing statements separated by automatic grid synchronization.</param>
    public static void SyncGrid(Action body) => GpuOnlyException.Throw();

    /// <summary>
    /// Marker method for JIT Constant Folding. Replaced with the literal macro value during runtime compilation.
    /// </summary>
    /// <param name="macroName">The name of the JIT macro parameter.</param>
    /// <returns>A typed placeholder evaluated at GPU compile-time.</returns>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Constant(string macroName) => GpuOnlyException.Throw<int>();

    /// <summary>Atomically adds an integer value to the target address and returns the old value.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to add.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the addition.</returns>
    [CudaIntrinsic("atomicAdd")]
    public static int AtomicAdd(ref int address, int val) => GpuOnlyException.Throw<int>();

    /// <summary>Atomically adds an unsigned integer value to the target address and returns the old value.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to add.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the addition.</returns>
    [CudaIntrinsic("atomicAdd")]
    public static uint AtomicAdd(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Atomically adds an unsigned 64-bit integer to the target address and returns the old value.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to add.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the addition.</returns>
    [CudaIntrinsic("atomicAdd")]
    public static ulong AtomicAdd(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();

    /// <summary>Atomically adds a floating-point value to the target address and returns the old value.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to add.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the addition.</returns>
    [CudaIntrinsic("atomicAdd")]
    public static float AtomicAdd(ref float address, float val) => GpuOnlyException.Throw<float>();

    /// <summary>Atomically subtracts an integer value from the target address and returns the old value.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to subtract.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the subtraction.</returns>
    [CudaIntrinsic("atomicSub")]
    public static int AtomicSub(ref int address, int val) => GpuOnlyException.Throw<int>();

    /// <summary>Atomically subtracts an unsigned integer value from the target address and returns the old value.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to subtract.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the subtraction.</returns>
    [CudaIntrinsic("atomicSub")]
    public static uint AtomicSub(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Adds two unsigned 32-bit integers with carry-in and carry-out propagation.</summary>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <param name="carry">The carry-in value (0 or 1), updated with the carry-out result.</param>
    /// <returns>The 32-bit addition result.</returns>
    [CudaIntrinsic("__gsharp_add_cc")]
    public static uint AddWithCarry(uint a, uint b, ref ulong carry) => GpuOnlyException.Throw<uint>();

    /// <summary>Subtracts two unsigned 32-bit integers with borrow-in and borrow-out propagation.</summary>
    /// <param name="a">The minuend operand.</param>
    /// <param name="b">The subtrahend operand.</param>
    /// <param name="borrow">The borrow-in value (0 or 1), updated with the borrow-out result.</param>
    /// <returns>The 32-bit subtraction result.</returns>
    [CudaIntrinsic("__gsharp_sub_cc")]
    public static uint SubWithBorrow(uint a, uint b, ref long borrow) => GpuOnlyException.Throw<uint>();

    /// <summary>Atomically computes bitwise OR on the target unsigned 32-bit integer address.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to OR.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the operation.</returns>
    [CudaIntrinsic("atomicOr")]
    public static uint AtomicOr(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Atomically computes bitwise OR on the target unsigned 64-bit integer address.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to OR.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the operation.</returns>
    [CudaIntrinsic("atomicOr")]
    public static ulong AtomicOr(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();

    /// <summary>Atomically computes the maximum value between the address content and the supplied value.</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to compare.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the operation.</returns>
    [CudaIntrinsic("atomicMax")]
    public static uint AtomicMax(ref uint address, uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Atomically computes the maximum value between the address content and the supplied value (64-bit).</summary>
    /// <param name="address">The reference to the memory location to modify.</param>
    /// <param name="val">The value to compare.</param>
    /// <returns>The original value stored at <paramref name="address"/> before the operation.</returns>
    [CudaIntrinsic("atomicMax")]
    public static ulong AtomicMax(ref ulong address, ulong val) => GpuOnlyException.Throw<ulong>();

    /// <summary>Reverses the bit order of a 32-bit unsigned integer using the <c>__brev</c> hardware instruction.</summary>
    /// <param name="val">The 32-bit unsigned integer whose bits are to be reversed.</param>
    /// <returns>The bit-reversed 32-bit unsigned integer.</returns>
    [CudaIntrinsic("__brev")]
    public static uint BitReverse(uint val) => GpuOnlyException.Throw<uint>();

    /// <summary>Computes the full 64-bit product of two 32-bit unsigned integers without overflow.</summary>
    /// <param name="a">The first 32-bit unsigned multiplicand.</param>
    /// <param name="b">The second 32-bit unsigned multiplicand.</param>
    /// <returns>The 64-bit product of <paramref name="a"/> and <paramref name="b"/>.</returns>
    [CudaIntrinsic("__gsharp_mul_wide")]
    public static ulong MultiplyWide(uint a, uint b) => GpuOnlyException.Throw<ulong>();

    /// <summary>Computes the high 64 bits of the product of two 64-bit unsigned integers (<c>__umul64hi</c>).</summary>
    /// <param name="a">The first 64-bit unsigned multiplicand.</param>
    /// <param name="b">The second 64-bit unsigned multiplicand.</param>
    /// <returns>The most significant 64 bits of the 128-bit product.</returns>
    [CudaIntrinsic("__umul64hi")]
    public static ulong MultiplyHigh(ulong a, ulong b) => GpuOnlyException.Throw<ulong>();

    /// <summary>Concatenates hi and lo, shifts left by shift, and extracts the most significant 32 bits.</summary>
    /// <param name="lo">The least significant 32 bits.</param>
    /// <param name="hi">The most significant 32 bits.</param>
    /// <param name="shift">The shift amount in bits (0-31).</param>
    /// <returns>The most significant 32 bits of the shifted 64-bit pair.</returns>
    [CudaIntrinsic("__funnelshift_l")]
    public static uint FunnelShiftLeft(uint lo, uint hi, uint shift) => GpuOnlyException.Throw<uint>();

    /// <summary>Concatenates hi and lo, shifts right by shift, and extracts the least significant 32 bits.</summary>
    /// <param name="lo">The least significant 32 bits.</param>
    /// <param name="hi">The most significant 32 bits.</param>
    /// <param name="shift">The shift amount in bits (0-31).</param>
    /// <returns>The least significant 32 bits of the shifted 64-bit pair.</returns>
    [CudaIntrinsic("__funnelshift_r")]
    public static uint FunnelShiftRight(uint lo, uint hi, uint shift) => GpuOnlyException.Throw<uint>();

    /// <summary>
    /// Injects raw inline PTX assembly or CUDA C++ instructions directly into the transpiled output.
    /// </summary>
    /// <param name="ptx">The raw PTX or C++ string to inject.</param>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static void InlinePtx(string ptx) => GpuOnlyException.Throw();

    /// <summary>
    /// Transpilation macro that unrolls a loop body at compile-time by duplicating the source code across the specified range.
    /// </summary>
    /// <param name="start">The constant loop starting index.</param>
    /// <param name="count">The constant loop iteration count.</param>
    /// <param name="body">The loop body delegate.</param>
    /// <remarks>
    /// <para><b>Usage Scenario:</b></para>
    /// <para>This method should be used strictly when the loop variable needs to be evaluated as a compile-time constant literal (e.g., <c>0</c>, <c>1</c>, <c>2</c>). This is essential when passing the index to methods that enforce strict constant evaluation, such as statically sized shared memory allocations.</para>
    /// <para><b>Caution:</b></para>
    /// <para>Because this macro performs literal source code duplication during transpilation, using it for loops with high iteration counts will drastically bloat the generated C++ code and may cause the backend compiler to crash. For general performance tuning and ILP improvements, use <see cref="Hint.Unroll(int)"/> instead.</para>
    /// <para>Note that executing this method on the C# CPU host will inevitably allocate a delegate closure, which violates zero-allocation principles.</para>
    /// </remarks>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static void ForUnroll(int start, int count, Action<int> body) => GpuOnlyException.Throw();

    /// <summary>Delegate for compile-time unrolled loops with a passed execution context.</summary>
    /// <typeparam name="TContext">The state structure type passed by reference across loop iterations.</typeparam>
    /// <param name="ctx">The execution context passed by reference.</param>
    /// <param name="index">The zero-based unrolled loop iteration index.</param>
    public delegate void UnrollAction<TContext>(ref TContext ctx, int index);

    /// <summary>
    /// Transpilation macro that unrolls a loop body at compile-time with context state propagation.
    /// </summary>
    /// <typeparam name="TContext">The state structure type passed by reference across loop iterations.</typeparam>
    /// <param name="ctx">The execution context passed by reference.</param>
    /// <param name="start">The constant loop starting index.</param>
    /// <param name="count">The constant loop iteration count.</param>
    /// <param name="body">The loop body delegate receiving the context and index.</param>
    /// <inheritdoc cref="ForUnroll(int, int, System.Action{int})"/>
    public static void ForUnroll<TContext>(ref TContext ctx, int start, int count, UnrollAction<TContext> body) => GpuOnlyException.Throw();

    /// <summary>
    /// Generates a standard CUDA Grid-Stride Loop over the specified bound across all threads and blocks.
    /// </summary>
    /// <param name="maxBounds">The total element count to process.</param>
    /// <param name="body">The loop body delegate receiving the current thread stride index.</param>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static void GridStrideLoop(int maxBounds, Action<int> body) => GpuOnlyException.Throw();

    /// <summary>Delegate for grid-stride loops with a passed execution context.</summary>
    /// <typeparam name="TContext">The state structure type passed by reference across thread loop strides.</typeparam>
    /// <param name="ctx">The execution context passed by reference.</param>
    /// <param name="index">The current stride index processed by this thread.</param>
    public delegate void GridStrideAction<TContext>(ref TContext ctx, int index);

    /// <summary>
    /// Generates a standard CUDA Grid-Stride Loop with context state propagation.
    /// </summary>
    /// <typeparam name="TContext">The state structure type passed by reference across thread loop strides.</typeparam>
    /// <param name="ctx">The execution context passed by reference.</param>
    /// <param name="maxBounds">The total element count to process.</param>
    /// <param name="body">The loop body delegate receiving the context and current stride index.</param>
    public static void GridStrideLoop<TContext>(ref TContext ctx, int maxBounds, GridStrideAction<TContext> body) => GpuOnlyException.Throw();

    /// <summary>
    /// Ensures that the enclosed action is executed exclusively by thread 0 of block 0 (<c>threadIdx.x == 0 &amp;&amp; blockIdx.x == 0</c>).
    /// </summary>
    /// <param name="body">The action to execute on a single thread.</param>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static void SingleThread(Action body) => GpuOnlyException.Throw();

#pragma warning disable IDE0051 // Intended for external kernel consumers
    // ReSharper disable UnusedMember.Global

    /// <summary>Returns a 32-bit mask of all currently active threads in the calling warp.</summary>
    /// <returns>A 32-bit bitmask where bit <c>i</c> corresponds to active lane <c>i</c>.</returns>
    [CudaIntrinsic("__activemask")]
    public static uint ActiveMask() => GpuOnlyException.Throw<uint>();

    /// <summary>Exchanges an integer variable between threads within a warp.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to broadcast from the source lane.</param>
    /// <param name="srcLane">The source lane index (0-31) whose value is broadcasted.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The value of <paramref name="var"/> held by thread <paramref name="srcLane"/>.</returns>
    [CudaIntrinsic("__shfl_sync")]
    public static int ShuffleSync(uint mask, int var, int srcLane, int width = 32) => GpuOnlyException.Throw<int>();

    /// <summary>Exchanges an unsigned integer variable between threads within a warp.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to broadcast from the source lane.</param>
    /// <param name="srcLane">The source lane index (0-31) whose value is broadcasted.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The value of <paramref name="var"/> held by thread <paramref name="srcLane"/>.</returns>
    [CudaIntrinsic("__shfl_sync")]
    public static uint ShuffleSync(uint mask, uint var, int srcLane, int width = 32) => GpuOnlyException.Throw<uint>();

    /// <summary>Exchanges an integer variable from a thread with a higher ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift down by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the higher-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_down_sync")]
    public static int ShuffleDownSync(uint mask, int var, uint delta, int width = 32) => GpuOnlyException.Throw<int>();

    /// <summary>Exchanges an unsigned integer variable from a thread with a higher ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift down by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the higher-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_down_sync")]
    public static uint ShuffleDownSync(uint mask, uint var, uint delta, int width = 32) => GpuOnlyException.Throw<uint>();

    /// <summary>Exchanges an integer variable from a thread with a lower ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift up by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the lower-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_up_sync")]
    public static int ShuffleUpSync(uint mask, int var, uint delta, int width = 32) => GpuOnlyException.Throw<int>();

    /// <summary>Exchanges an unsigned integer variable from a thread with a lower ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift up by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the lower-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_up_sync")]
    public static uint ShuffleUpSync(uint mask, uint var, uint delta, int width = 32) => GpuOnlyException.Throw<uint>();

    /// <summary>Exchanges an integer variable based on a bitwise XOR of the caller's lane ID.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to exchange.</param>
    /// <param name="laneMask">The bitmask used to XOR against the calling thread's lane ID.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The value from the XOR-calculated lane.</returns>
    [CudaIntrinsic("__shfl_xor_sync")]
    public static int ShuffleXorSync(uint mask, int var, int laneMask, int width = 32) => GpuOnlyException.Throw<int>();

    /// <summary>Exchanges a float variable between threads within a warp.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to broadcast from the source lane.</param>
    /// <param name="srcLane">The source lane index (0-31) whose value is broadcasted.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The value of <paramref name="var"/> held by thread <paramref name="srcLane"/>.</returns>
    [CudaIntrinsic("__shfl_sync")]
    public static float ShuffleSync(uint mask, float var, int srcLane, int width = 32) => GpuOnlyException.Throw<float>();

    /// <summary>Exchanges a float variable from a thread with a higher ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift down by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the higher-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_down_sync")]
    public static float ShuffleDownSync(uint mask, float var, uint delta, int width = 32) => GpuOnlyException.Throw<float>();

    /// <summary>Exchanges a float variable from a thread with a lower ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift up by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the lower-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_up_sync")]
    public static float ShuffleUpSync(uint mask, float var, uint delta, int width = 32) => GpuOnlyException.Throw<float>();

    /// <summary>Exchanges a float variable based on a bitwise XOR of the caller's lane ID.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to exchange.</param>
    /// <param name="laneMask">The bitmask used to XOR against the calling thread's lane ID.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The value from the XOR-calculated lane.</returns>
    [CudaIntrinsic("__shfl_xor_sync")]
    public static float ShuffleXorSync(uint mask, float var, int laneMask, int width = 32) => GpuOnlyException.Throw<float>();

    // ReSharper restore UnusedMember.Global
#pragma warning restore IDE0051
}

/// <summary>
/// Provides GPU warp-level communication intrinsics.
/// </summary>
public static class Warp
{
#pragma warning disable IDE0051
    // ReSharper disable UnusedMember.Global

    /// <summary>Exchanges an integer variable from a thread with a higher ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift down by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the higher-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_down_sync")]
    public static int ShuffleDownSync(uint mask, int var, uint delta, int width = 32) => GpuOnlyException.Throw<int>();

    /// <summary>Exchanges an unsigned integer variable from a thread with a higher ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift down by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the higher-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_down_sync")]
    public static uint ShuffleDownSync(uint mask, uint var, uint delta, int width = 32) => GpuOnlyException.Throw<uint>();

    /// <summary>Exchanges a floating-point variable from a thread with a higher ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift down by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the higher-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_down_sync")]
    public static float ShuffleDownSync(uint mask, float var, uint delta, int width = 32) => GpuOnlyException.Throw<float>();

    /// <summary>Exchanges a double-precision floating-point variable from a thread with a higher ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift down by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the higher-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_down_sync")]
    public static double ShuffleDownSync(uint mask, double var, uint delta, int width = 32) => GpuOnlyException.Throw<double>();

    /// <summary>Exchanges an unsigned integer variable from a thread with a lower ID relative to the caller.</summary>
    /// <param name="mask">The 32-bit thread participation bitmask.</param>
    /// <param name="var">The source value to shift.</param>
    /// <param name="delta">The positive lane index offset to shift up by.</param>
    /// <param name="width">The sub-warp width in threads (power of 2, default 32).</param>
    /// <returns>The shifted value from the lower-index lane, or the original value if out of bounds.</returns>
    [CudaIntrinsic("__shfl_up_sync")]
    public static uint ShuffleUpSync(uint mask, uint var, uint delta, int width = 32) => GpuOnlyException.Throw<uint>();

    // ReSharper restore UnusedMember.Global
#pragma warning restore IDE0051
}

