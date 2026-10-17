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
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int X => ManagedGpuContext.IsActive ? ManagedGpuContext.ThreadIdxX : GpuOnlyException.Throw<int>();

    /// <summary>Gets the thread index along the Y axis (<c>threadIdx.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Y => ManagedGpuContext.IsActive ? ManagedGpuContext.ThreadIdxY : GpuOnlyException.Throw<int>();

    /// <summary>Gets the thread index along the Z axis (<c>threadIdx.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Z => ManagedGpuContext.IsActive ? ManagedGpuContext.ThreadIdxZ : GpuOnlyException.Throw<int>();
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
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int X => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockIdxX : GpuOnlyException.Throw<int>();

    /// <summary>Gets the block index along the Y axis (<c>blockIdx.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Y => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockIdxY : GpuOnlyException.Throw<int>();

    /// <summary>Gets the block index along the Z axis (<c>blockIdx.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Z => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockIdxZ : GpuOnlyException.Throw<int>();
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
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int X => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockDimX : GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of threads per block along the Y axis (<c>blockDim.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Y => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockDimY : GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of threads per block along the Z axis (<c>blockDim.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Z => ManagedGpuContext.IsActive ? ManagedGpuContext.BlockDimZ : GpuOnlyException.Throw<int>();
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
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int X => ManagedGpuContext.IsActive ? ManagedGpuContext.GridDimX : GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of blocks per grid along the Y axis (<c>gridDim.y</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown inside of a managed execution context if disabled.</exception>
    public static int Y => ManagedGpuContext.IsActive ? ManagedGpuContext.GridDimY : GpuOnlyException.Throw<int>();

    /// <summary>Gets the number of blocks per grid along the Z axis (<c>gridDim.z</c>).</summary>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    public static int Z => ManagedGpuContext.IsActive ? ManagedGpuContext.GridDimZ : GpuOnlyException.Throw<int>();
}

/// <summary>
/// Provides hardware intrinsics, synchronization primitives, atomic operations, and macro markers for GPU kernels.
/// </summary>
// ReSharper disable once UnusedType.Global -- Public API exposed for GPU kernels
// ReSharper disable once UnusedMember.Global -- Public API exposed for GPU kernels
#pragma warning disable IDE0051 // Justification: Public API exposed for library consumers
public static partial class Gpu
{
    // NOTE FOR AI AGENTS & DEVELOPERS:
    // DO NOT declare hardware or warp intrinsics manually in this file.
    // All GPU and Warp intrinsics are code-generated from 'src/NativeApi/ptx_intrinsics.yml' at compile time.
    // To add or modify hardware intrinsics, declare them in the YAML file instead.
    // This file is strictly reserved for compiler macros (e.g., ForUnroll, Hint) and managed simulation hooks.

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
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a managed execution context.</exception>
    [CudaIntrinsic("__syncthreads")]
    public static void SyncThreads()
    {
        if (!ManagedGpuContext.IsActive)
        {
            GpuOnlyException.Throw();
        }
    }

    /// <summary>
    /// Executes the provided action, automatically inserting a grid synchronization (<c>cg::this_grid().sync()</c>)
    /// between each statement in the action body.
    /// </summary>
    /// <param name="body">The action containing statements separated by automatic grid synchronization.</param>
    public static void SyncGrid(Action body) => GpuOnlyException.Throw();

    /// <summary>
    /// Marker method for compile-time constant macro expansion. Replaced with the literal macro value during transpilation.
    /// </summary>
    /// <param name="macroName">The name of the constant macro parameter.</param>
    /// <returns>A typed placeholder evaluated at GPU compile-time.</returns>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Constant(string macroName) => GpuOnlyException.Throw<int>();
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

    // Removed Warp-related communication methods from Gpu class.
    // They are now source-generated in the Warp class by IntrinsicsGenerator.
#pragma warning restore IDE0051
}

