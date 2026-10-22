using Kassyi.GSharp.Naming;
namespace Kassyi.GSharp.Compiler;

/// <summary>Identifiers the compiler emits into generated host and device code.</summary>
/// <remarks>
/// GSH0055 rejects user identifiers containing <c>__</c>, so names under the <c>__gsharp_</c> prefix cannot collide with
/// user code. <c>Resources/Prelude/WarpAndHints.hpp</c> spells <see cref="Context"/>, <see cref="WarpExchange"/>,
/// <see cref="WarpShuffleCount"/>, <see cref="AllContexts"/> and <see cref="Root"/> literally; <c>PreludeInternalNamesTests</c> keeps the two in sync.
/// A name that a single emitter uses may stay a constant in that file, but it must not start with <see cref="RenamedIdentifierPrefix"/> or <see cref="RenamedMemberPrefix"/>.
/// </remarks>
internal static class GSharpInternalNames
{
    // Family prefixes for renamed user declarations (see CppNameResolver). No other constant may start with them.
    public const string RenamedIdentifierPrefix = GSharpNameEncoding.IdentifierPrefix;
    public const string RenamedMemberPrefix = GSharpNameEncoding.MemberPrefix;

    // Names that belong to one kernel, followed by the body of its entry point name (GlobalCppNames.GetKernelDerivedName).
    public const string KernelLaunchExportPrefix = "__gsharp_launch_";
    public const string KernelContextPrefix = "__gsharp_context_";
    public const string KernelStateMachinePrefix = "__gsharp_statemachine_";

    // Per-thread state machine context (CPU dialects).
    public const string Context = "__gsharp_ctx";
    public const string State = "__gsharp_state";
    public const string YieldedForGridSync = "__gsharp_yielded_for_grid_sync";
    public const string WarpExchange = "__gsharp_warp_exchange";
    public const string WarpShuffleCount = "__gsharp_warp_shuffle_count";
    public const string AllContexts = "__gsharp_all_contexts";

    // Device functions that reach a barrier (CPU dialects). Each one is a state machine with its own frame; the kernel
    // context is passed down as the root, which owns the warp exchange of the thread.
    public const string Root = "__gsharp_root";
    public const string FrameTypePrefix = "__gsharp_frame_";
    public const string CallFramePrefix = "__gsharp_call_frame_";
    public const string CopiedParameterPrefix = "__gsharp_in_";
    public const string SuspendingCallResult = "__gsharp_result";

    /// <summary>Marks a call to a device function that may suspend: <c>__gsharp_resume(call, frameIndex[, result])</c>.</summary>
    public const string ResumeCall = "__gsharp_resume";

    /// <summary>Marks the current thread's state machine as finished.</summary>
    public const string StateTermination = State + " = -1;";

    // Lowering temporaries and labels. Prefixes are followed by a counter or a user-derived name.
    public const string LabelPrefix = "__gsharp_";
    public const string WarpTempPrefix = "__gsharp_warp_temp_";
    public const string CallTempPrefix = "__gsharp_call_temp_";
    public const string ArgumentTempPrefix = "__gsharp_arg_temp_";
    public const string ConditionTempPrefix = "__gsharp_cond_temp_";
    public const string AggregateTemp = "__gsharp_tmp";
    public const string TupleTempPrefix = "__gsharp_tuple_";
    public const string CoalesceTarget = "__gsharp_coalesce_target";
    public const string SwitchStatePrefix = "__gsharp_switch_";

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
    public const string AutoResult = "__gsharp_auto_result";
    public const string AutoResultSize = AutoResult + "_size";

    // Device-side locals of a kernel that returns a value.
    public const string GlobalThreadIndex = "__gsharp_global_tid";

    // CPU C++ launcher (`__gsharp_launch_*`): launch dimensions, block/thread coordinates and loop state.
    public const string LaunchGridDimX = "__gsharp_grid_dim_x";
    public const string LaunchGridDimY = "__gsharp_grid_dim_y";
    public const string LaunchGridDimZ = "__gsharp_grid_dim_z";
    public const string LaunchBlockDimX = "__gsharp_block_dim_x";
    public const string LaunchBlockDimY = "__gsharp_block_dim_y";
    public const string LaunchBlockDimZ = "__gsharp_block_dim_z";
    public const string LaunchBlockX = "__gsharp_bx";
    public const string LaunchBlockY = "__gsharp_by";
    public const string LaunchBlockZ = "__gsharp_bz";
    public const string LaunchThreadX = "__gsharp_tx";
    public const string LaunchThreadY = "__gsharp_ty";
    public const string LaunchThreadZ = "__gsharp_tz";
    public const string LaunchContextItem = "__gsharp_launch_ctx";
    public const string LaunchAnyActive = "__gsharp_any_active";
}
