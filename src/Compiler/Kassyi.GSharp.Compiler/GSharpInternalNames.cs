namespace Kassyi.GSharp.Compiler;

/// <summary>Identifiers the compiler emits into generated host and device code.</summary>
/// <remarks>
/// GSH0057 rejects user identifiers containing <c>__</c>, so names under the <c>__gsharp_</c> prefix cannot collide with
/// user code. <c>Resources/Prelude/WarpAndHints.hpp</c> spells <see cref="Context"/>, <see cref="WarpExchange"/> and
/// <see cref="AllContexts"/> literally; <c>PreludeInternalNamesTests</c> keeps the two in sync.
/// </remarks>
internal static class GSharpInternalNames
{
    // Per-thread state machine context (CPU dialects).
    public const string Context = "__gsharp_ctx";
    public const string State = "__gsharp_state";
    public const string YieldedForGridSync = "__gsharp_yielded_for_grid_sync";
    public const string WarpExchange = "__gsharp_warp_exchange";
    public const string AllContexts = "__gsharp_all_contexts";

    /// <summary>Marks the current thread's state machine as finished.</summary>
    public const string StateTermination = State + " = -1;";

    // Lowering temporaries and labels. Prefixes are followed by a counter or a user-derived name.
    public const string LabelPrefix = "__gsharp_";
    public const string WarpTempPrefix = "__gsharp_warp_temp_";
    public const string AggregateTemp = "__gsharp_tmp";
    public const string TupleTempPrefix = "__gsharp_tuple_";
    public const string CoalesceTarget = "__gsharp_coalesce_target";

    // Host launcher locals. Kernel parameters share their scope.
    public const string LaunchContexts = "__gsharp_contexts";
    public const string LaunchError = "__gsharp_launch_error";
    public const string TotalBlocks = "__gsharp_total_blocks";
    public const string ThreadsPerBlock = "__gsharp_threads_per_block";
    public const string BlockLoopIndex = "__gsharp_block_index";
    public const string ThreadLoopIndex = "__gsharp_thread_index";
    public const string ContextThreadIndex = "__gsharp_ctx_tid";
    public const string LinearThreadIndex = "__gsharp_linear_tid";
    public const string SharedMemoryBufferPrefix = "__gsharp_smem_buffer_";
    public const string CudaContextScope = "__gsharp_cuda_context_scope";
}
