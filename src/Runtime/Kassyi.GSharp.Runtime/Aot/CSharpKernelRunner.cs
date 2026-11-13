using System.ComponentModel;
using System.Runtime.ExceptionServices;

namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>
/// Runs the C# body of a kernel once per GPU thread of a launch, one thread after another, for
/// <see cref="KernelExecution.CSharp"/>. For generated code only: the generated <c>Launch{Kernel}</c> method calls it
/// inside a <see cref="GpuExecution.Use"/> scope of <see cref="KernelExecution.CSharp"/>.
/// </summary>
/// <remarks>
/// The threads run on a host thread of their own, which starts with no thread context and ends with none, so a failing
/// GPU thread never leaves a context behind on the caller's thread. Each GPU thread runs with its coordinates set, as
/// <c>ThreadIndex</c>, <c>BlockIndex</c>, <c>BlockDimension</c> and <c>GridDimension</c> read them; the operations
/// that need the other threads of the block or grid throw (<c>Gpu.SyncThreads()</c>, <c>SharedMemory&lt;T&gt;</c>).
/// </remarks>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class CSharpKernelRunner
{
    // The order of the GPU threads in this asynchronous flow: null for the order of their linear index. The
    // differential tests run a kernel in several orders; a result that depends on the order is a data race
    private static readonly AsyncLocal<Func<CSharpLaunchDimensions, int[]>?> s_threadOrder = new();

    /// <summary>
    /// Runs <paramref name="thread"/> once per GPU thread of the launch, in the order of the linear thread index
    /// (blocks in the order x, y, z, and the threads of a block in the same order), each with its thread context set.
    /// For generated code only.
    /// </summary>
    /// <param name="gridDimX">The number of blocks in x.</param>
    /// <param name="gridDimY">The number of blocks in y.</param>
    /// <param name="gridDimZ">The number of blocks in z.</param>
    /// <param name="blockDimX">The number of threads per block in x.</param>
    /// <param name="blockDimY">The number of threads per block in y.</param>
    /// <param name="blockDimZ">The number of threads per block in z.</param>
    /// <param name="thread">The call of the kernel, given the linear index of the GPU thread.</param>
    /// <exception cref="ArgumentOutOfRangeException">A dimension is 0.</exception>
    /// <remarks>The first exception of a GPU thread ends the run and is rethrown with its stack trace.</remarks>
    public static void Run(uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY, uint blockDimZ,
        Action<long> thread)
    {
        ArgumentNullException.ThrowIfNull(thread);
        var shape = new LaunchGeometry(gridDimX, gridDimY, gridDimZ, blockDimX, blockDimY, blockDimZ);
        var order = s_threadOrder.Value;
        ExceptionDispatchInfo? failure = null;
        var host = new Thread(() =>
        {
            try
            {
                RunThreads(shape, order, thread);
            }
            catch (Exception ex)
            {
                failure = ExceptionDispatchInfo.Capture(ex);
            }
            finally
            {
                ManagedGpuContext.EndCSharpExecution();
            }
        })
        {
            Name = "G# C# kernel execution",
            IsBackground = true
        };
        host.Start();
        host.Join();
        failure?.Throw();
    }

    /// <summary>
    /// Creates the exception of a launch under <see cref="KernelExecution.CSharp"/> that cannot run as C#, such as a
    /// kernel that reaches a barrier. For generated code only.
    /// </summary>
    /// <param name="kernel">The kernel, as <c>Type.Method</c>.</param>
    /// <param name="reason">Why the kernel cannot run as C#, as the end of a sentence.</param>
    /// <returns>The exception to throw.</returns>
    public static CSharpExecutionUnsupportedException CannotRun(string kernel, string reason) =>
        new($"G#: {kernel} cannot run under KernelExecution.CSharp: {reason}. Launch it under " +
            "KernelExecution.Default or KernelExecution.Managed.");

    /// <summary>
    /// Runs the GPU threads of the launches of the current asynchronous flow in the order that
    /// <paramref name="order"/> gives for the dimensions of a launch (a permutation of the linear indices), until the
    /// returned scope is disposed. For the differential tests, which run a kernel in several orders and check that
    /// each launch has the dimensions they expect.
    /// </summary>
    /// <param name="order">
    /// The linear thread indices in the order to run them, given the dimensions of the launch.
    /// </param>
    /// <returns>The scope, which restores the previous order when disposed.</returns>
    internal static IDisposable UseThreadOrder(Func<CSharpLaunchDimensions, int[]> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var previous = s_threadOrder.Value;
        s_threadOrder.Value = order;
        return new ThreadOrderScope(previous);
    }

    private static void RunThreads(in LaunchGeometry shape, Func<CSharpLaunchDimensions, int[]>? order,
        Action<long> thread)
    {
        if (order is null)
        {
            for (long id = 0; id < shape.ThreadCount; id++)
            {
                RunThread(shape, id, thread);
            }

            return;
        }

        foreach (var id in GetOrder(shape, order))
        {
            RunThread(shape, id, thread);
        }
    }

    private static void RunThread(in LaunchGeometry shape, long id, Action<long> thread)
    {
        ManagedGpuContext.BeginCSharpThread(shape.ContextOf(id));
        thread(id);
    }

    // The ids of a custom order, checked to be a permutation, so that an order never skips or repeats a thread
    private static int[] GetOrder(in LaunchGeometry shape, Func<CSharpLaunchDimensions, int[]> order)
    {
        if (shape.ThreadCount > int.MaxValue)
        {
            throw new InvalidOperationException(
                $"A custom thread order covers at most {int.MaxValue} threads; the launch has {shape.ThreadCount}.");
        }

        var count = (int)shape.ThreadCount;
        var ids = order(shape.Dimensions(count));
        var seen = new bool[count];
        if (ids.Length != count)
        {
            throw new InvalidOperationException($"The thread order gave {ids.Length} ids for {count} threads.");
        }

        foreach (var id in ids)
        {
            if ((uint)id >= (uint)count || seen[id])
            {
                throw new InvalidOperationException($"The thread order is not a permutation of 0..{count - 1}: {id}.");
            }

            seen[id] = true;
        }

        return ids;
    }

    // The dimensions of a launch and the coordinates of a linear thread index
    private readonly struct LaunchGeometry
    {
        private readonly long _gridX;
        private readonly long _gridY;
        private readonly long _gridZ;
        private readonly long _blockX;
        private readonly long _blockY;
        private readonly long _blockZ;
        private readonly long _threadsPerBlock;

        public LaunchGeometry(uint gridDimX, uint gridDimY, uint gridDimZ, uint blockDimX, uint blockDimY,
            uint blockDimZ)
        {
            ArgumentOutOfRangeException.ThrowIfZero(gridDimX);
            ArgumentOutOfRangeException.ThrowIfZero(gridDimY);
            ArgumentOutOfRangeException.ThrowIfZero(gridDimZ);
            ArgumentOutOfRangeException.ThrowIfZero(blockDimX);
            ArgumentOutOfRangeException.ThrowIfZero(blockDimY);
            ArgumentOutOfRangeException.ThrowIfZero(blockDimZ);
            (_gridX, _gridY, _gridZ) = (gridDimX, gridDimY, gridDimZ);
            (_blockX, _blockY, _blockZ) = (blockDimX, blockDimY, blockDimZ);
            _threadsPerBlock = checked(_blockX * _blockY * _blockZ);
            ThreadCount = checked(_gridX * _gridY * _gridZ * _threadsPerBlock);
        }

        public long ThreadCount { get; }

        public CSharpLaunchDimensions Dimensions(int threadCount) => new(
            (uint)_gridX, (uint)_gridY, (uint)_gridZ, (uint)_blockX, (uint)_blockY, (uint)_blockZ, threadCount);

        // The same arrangement as the managed launcher and the GPU: x fastest, for the threads and for the blocks
        public GpuThreadContext ContextOf(long id)
        {
            var block = id / _threadsPerBlock;
            var t = id % _threadsPerBlock;
            return new GpuThreadContext(
                (int)(t % _blockX), (int)(t / _blockX % _blockY), (int)(t / (_blockX * _blockY)),
                (int)(block % _gridX), (int)(block / _gridX % _gridY), (int)(block / (_gridX * _gridY)),
                (int)_blockX, (int)_blockY, (int)_blockZ,
                (int)_gridX, (int)_gridY, (int)_gridZ
            );
        }
    }

    private sealed class ThreadOrderScope(Func<CSharpLaunchDimensions, int[]>? previous) : IDisposable
    {
        public void Dispose() => s_threadOrder.Value = previous;
    }
}

/// <summary>The dimensions of a launch under <see cref="KernelExecution.CSharp"/> and its number of threads.</summary>
/// <param name="GridDimX">The number of blocks in x.</param>
/// <param name="GridDimY">The number of blocks in y.</param>
/// <param name="GridDimZ">The number of blocks in z.</param>
/// <param name="BlockDimX">The number of threads per block in x.</param>
/// <param name="BlockDimY">The number of threads per block in y.</param>
/// <param name="BlockDimZ">The number of threads per block in z.</param>
/// <param name="ThreadCount">The number of GPU threads of the launch.</param>
internal readonly record struct CSharpLaunchDimensions(
    uint GridDimX,
    uint GridDimY,
    uint GridDimZ,
    uint BlockDimX,
    uint BlockDimY,
    uint BlockDimZ,
    int ThreadCount
);
