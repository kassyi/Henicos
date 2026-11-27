using System.ComponentModel;

namespace Kassyi.GSharp.Core;

public static partial class ManagedGpuContext
{
    [ThreadStatic]
    private static ManagedStackThread? t_stackThread;

    /// <summary>
    /// Waits at a block barrier of the managed stack model until every running thread of the block has reached its
    /// barrier or finished. For generated code only.
    /// </summary>
    /// <exception cref="InvalidOperationException">Called outside a managed stack launch.</exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void __gsharp_stack_block_barrier()
    {
        if (t_stackThread is not { Launch: { } launch } self)
        {
            throw new InvalidOperationException(
                "G#: a block barrier of the stack model was reached outside a managed launch");
        }

        self.AtBlockBarrier = true;
        ManagedBaton.HandOverNext(self, launch);
        if (launch.Cancelled)
        {
            throw new ManagedLaunchCancelledException();
        }
    }

    /// <summary>
    /// Waits at a grid sync of the managed stack model until every block of the grid has reached the end of the phase.
    /// For generated code only.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Called outside a managed stack launch, or the launch is not <c>[GpuKernel(Cooperative = true)]</c>.
    /// </exception>
    [EditorBrowsable(EditorBrowsableState.Never)]
    // ReSharper disable once UnusedMember.Global -- Public API for code generators
    public static void __gsharp_stack_grid_barrier()
    {
        if (t_stackThread is not { Launch: { } launch } self)
        {
            throw new InvalidOperationException(
                "G#: a grid sync of the stack model was reached outside a managed launch");
        }

        if (!launch.Cooperative)
        {
            throw GridSyncInKernelThatIsNotCooperative(launch.KernelName);
        }

        self.AtGridSync = true;
        ManagedBaton.HandOverNext(self, launch);
        if (launch.Cancelled)
        {
            throw new ManagedLaunchCancelledException();
        }
    }

    internal static void BindStackThread(ManagedStackThread self)
    {
        if (self.Launch?.State is not { } state)
        {
            throw new InvalidOperationException("G#: a stack thread cannot run without a launch block state");
        }

        t_stackThread = self;
        t_blockState = state;
        SetThreadContext(self.Context);
        ResetTypeKeyScope();
    }

    internal static void UnbindStackThread()
    {
        ClearThreadContext();
        ResetTypeKeyScope();
        t_blockState = null;
        t_stackThread = null;
    }

    internal static ManagedBlockState ShareBlockState() => BlockState;

    // Gives the calling thread <state> as its block state and returns the one it had (null when it had none yet): a
    // launch that starts while another launch of the same thread waits keeps the outer launch's shared memory intact
    internal static ManagedBlockState? SwapBlockState(ManagedBlockState? state)
    {
        var previous = t_blockState;
        t_blockState = state;
        return previous;
    }
}
