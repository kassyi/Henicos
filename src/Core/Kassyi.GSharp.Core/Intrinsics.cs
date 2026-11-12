namespace Kassyi.GSharp.Core;

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
        /// <param name="factor">
        /// The unroll factor, emitted as <c>#pragma unroll factor</c>. 0 emits a bare <c>#pragma unroll</c>: NVCC
        /// unrolls the loop completely when its trip count is a compile-time constant and otherwise does not unroll it.
        /// </param>
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
    /// <exception cref="GpuOnlyException">
    /// Thrown when evaluated on the host CPU outside of a managed execution context, or in a kernel launched under
    /// <c>KernelExecution.CSharp</c>, which runs one GPU thread at a time.
    /// </exception>
    [CudaIntrinsic("__syncthreads")]
    public static void SyncThreads()
    {
        ManagedGpuContext.ThrowIfCSharpExecution("Gpu.SyncThreads()");
        if (!ManagedGpuContext.IsActive)
        {
            GpuOnlyException.Throw();
        }
    }

    /// <summary>
    /// Synchronizes all threads across the entire cooperative grid: no thread goes on until every thread of the grid
    /// has reached the barrier, and the writes before it are visible to every thread after it.
    /// </summary>
    /// <param name="mode">
    /// What the barrier does after a thread of the launch failed (<see cref="GridSyncMode"/>); a compile-time constant.
    /// Both modes throw the recorded exception and leave no thread waiting. <see cref="GridSyncMode.Deterministic"/>,
    /// the default, lets no thread run the code after a barrier reached after the failure;
    /// <see cref="GridSyncMode.Fast"/> saves a read on each barrier and may let the other blocks run one more interval
    /// first. The native CPU build and the managed path treat both modes alike.
    /// </param>
    /// <remarks>
    /// Only a <c>[GpuKernel(Cooperative = true)]</c> kernel may reach it (GSH0030), and every thread of the grid must
    /// reach it.
    /// </remarks>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside of a kernel.</exception>
    [CudaIntrinsic("__gsharp_grid_sync")]
    public static void SyncGrid(GridSyncMode mode = GridSyncMode.Deterministic)
    {
        ManagedGpuContext.ThrowIfCSharpExecution("Gpu.SyncGrid()");
        GpuOnlyException.Throw();
    }

    /// <summary>
    /// Executes the provided action, automatically inserting a grid synchronization
    /// (<see cref="SyncGrid(GridSyncMode)"/>, <see cref="GridSyncMode.Deterministic"/>) between the top-level
    /// statements of the action body.
    /// </summary>
    /// <param name="body">The action containing statements separated by automatic grid synchronization.</param>
    /// <remarks>
    /// <para>
    /// A nested block (<c>for</c>, <c>if</c>, a loop macro, a local function) is one statement: nothing inside it is
    /// synchronized unless it contains its own <c>Gpu.SyncGrid</c> call. No synchronization is inserted after the last
    /// statement, next to an explicit <see cref="SyncGrid(GridSyncMode)"/>, or around an empty statement or a local
    /// function declaration. On the CPU dialects the body runs as the states of the kernel's state machine, one state
    /// per gap.
    /// </para>
    /// <para>
    /// A <c>return</c> in the body ends the body for the thread, as it ends the lambda in C#; the thread still takes part
    /// in the grid synchronizations of the remaining statements.
    /// </para>
    /// </remarks>
    public static void SyncGrid(Action body)
    {
        ManagedGpuContext.ThrowIfCSharpExecution("Gpu.SyncGrid(body)");
        GpuOnlyException.Throw();
    }

    /// <summary>
    /// Marker method for compile-time constant macro expansion. Replaced with the literal macro value during transpilation.
    /// </summary>
    /// <param name="macroName">The name of the constant macro parameter.</param>
    /// <returns>A typed placeholder evaluated at GPU compile-time.</returns>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU.</exception>
    public static int Constant(string macroName) => GpuOnlyException.Throw<int>();

    /// <summary>
    /// Returns <paramref name="array"/> as a non-nullable array, or throws <see cref="NullReferenceException"/> when it
    /// is <see langword="null"/>.
    /// </summary>
    /// <typeparam name="T">The element type of the array.</typeparam>
    /// <param name="array">
    /// An array that may be <see langword="null"/>, such as a nullable array field of a struct argument.
    /// </param>
    /// <returns><paramref name="array"/> itself.</returns>
    /// <remarks>
    /// <para>
    /// Read a nullable array once through this method into a local, and use the local: the compiler knows that a
    /// local initialized from <c>Gpu.NotNull</c> and never assigned holds an array, so its later reads, and the calls
    /// that pass it where a device function parameter is not nullable, need no null check of their own.
    /// </para>
    /// <code>
    /// int[] data = Gpu.NotNull(context.Data);   // throws here when context.Data is null
    /// for (int i = 0; i &lt; data.Length; i++) { sum += data[i]; }
    /// </code>
    /// <para>
    /// The method works in kernels, in device functions and on the host. A <see langword="null"/> optional kernel
    /// argument, which reads as an empty array, is still <see langword="null"/> here and throws, and so does an empty
    /// one, which the launchers pass as <see langword="null"/>.
    /// </para>
    /// </remarks>
    /// <exception cref="NullReferenceException">
    /// <paramref name="array"/> is <see langword="null"/>. In a kernel the exception carries the place of the call, as
    /// the exception of any other read of a <see langword="null"/> array does.
    /// </exception>
    // [NotNull]: after the call the compiler knows that the argument (a field, a local) is not null either
    public static T[] NotNull<T>([System.Diagnostics.CodeAnalysis.NotNull] T[]? array) =>
        array ?? throw new NullReferenceException(NotNullMessage);

    // The message of the host and managed exception; the device reports the same text after "G#: ", with the place
    private const string NotNullMessage = "Gpu.NotNull was given a null array";

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
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside managed execution.</exception>
    public static void ForUnroll(int start, int count, Action<int> body)
    {
        if (!ManagedGpuContext.IsActive)
        {
            GpuOnlyException.Throw();
        }

        for (int index = start; index < start + count; index++)
        {
            body(index);
        }
    }

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
    public static void ForUnroll<TContext>(ref TContext ctx, int start, int count, UnrollAction<TContext> body)
    {
        if (!ManagedGpuContext.IsActive)
        {
            GpuOnlyException.Throw();
        }

        for (int index = start; index < start + count; index++)
        {
            body(ref ctx, index);
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> once for each index of <c>[0, maxBounds)</c>, spread over every thread of the launch
    /// (a grid-stride loop).
    /// </summary>
    /// <param name="maxBounds">The total element count to process. It is evaluated once; a negative count runs no
    /// iteration.</param>
    /// <param name="body">The loop body delegate receiving the current thread stride index.</param>
    /// <remarks>
    /// Each thread starts at its linear rank in the whole launch (x fastest, then y, then z, and the blocks in the same
    /// order, as cooperative groups <c>grid.thread_rank()</c>) and steps by the number of threads in the launch
    /// (<c>grid.num_threads()</c>). Every index is therefore visited exactly once for every launch shape, including 2D and
    /// 3D blocks and grids, and for launches of 2^31 threads or more. To give each row of a 2D launch its own loop, write
    /// the loop over <c>ThreadIndex.X</c> and <c>BlockIndex.X</c> yourself.
    /// </remarks>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside managed execution.</exception>
    public static void GridStrideLoop(int maxBounds, Action<int> body)
    {
        if (!ManagedGpuContext.IsActive)
        {
            GpuOnlyException.Throw();
        }

        uint count = ManagedGpuContext.GridStrideCount(maxBounds);
        uint step = ManagedGpuContext.GridStrideStep();
        for (uint index = ManagedGpuContext.GridStrideStart(); index < count; index += step)
        {
            body((int)index);
        }
    }

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
    /// <inheritdoc cref="GridStrideLoop(int, System.Action{int})"/>
    public static void GridStrideLoop<TContext>(ref TContext ctx, int maxBounds, GridStrideAction<TContext> body)
    {
        if (!ManagedGpuContext.IsActive)
        {
            GpuOnlyException.Throw();
        }

        uint count = ManagedGpuContext.GridStrideCount(maxBounds);
        uint step = ManagedGpuContext.GridStrideStep();
        for (uint index = ManagedGpuContext.GridStrideStart(); index < count; index += step)
        {
            body(ref ctx, (int)index);
        }
    }

    /// <summary>
    /// Runs <paramref name="body"/> once per launch: only the thread whose thread and block indices are 0 on all three
    /// axes (<c>threadIdx.x</c>, <c>.y</c>, <c>.z</c> and <c>blockIdx.x</c>, <c>.y</c>, <c>.z</c>) runs it, for every
    /// block and grid shape.
    /// </summary>
    /// <param name="body">The action to execute on a single thread.</param>
    /// <remarks>
    /// The body runs on CUDA, on the native CPU path and on the managed CPU path. The compiler replaces the call with
    /// this condition; a device function that the managed path calls without rewriting it calls this method, which
    /// checks the same condition.
    /// A <c>return</c> in the body leaves the body only, as in C#: the statements after <c>Gpu.SingleThread</c> still
    /// run on that thread.
    /// </remarks>
    /// <exception cref="GpuOnlyException">Thrown when evaluated on the host CPU outside a kernel launch.</exception>
    public static void SingleThread(Action body)
    {
        if (!ManagedGpuContext.IsActive)
        {
            GpuOnlyException.Throw();
        }

        if ((ManagedGpuContext.ThreadIdxX | ManagedGpuContext.ThreadIdxY | ManagedGpuContext.ThreadIdxZ |
             ManagedGpuContext.BlockIdxX | ManagedGpuContext.BlockIdxY | ManagedGpuContext.BlockIdxZ) == 0)
        {
            body();
        }
    }

    // Gpu.Unsafe (Malloc / Free) is generated from src/NativeApi/device_heap_intrinsics.yml.

    // Removed Warp-related communication methods from Gpu class.
    // They are now source-generated in the Warp class by IntrinsicsGenerator.
#pragma warning restore IDE0051
}

