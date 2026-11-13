namespace Kassyi.GSharp.Runtime.Aot;

/// <summary>What <see cref="DynamicSharedMemoryOptIn"/> does before a launch.</summary>
internal enum SharedMemoryOptInAction
{
    /// <summary>The function already allows the request; no driver call.</summary>
    None,

    /// <summary>
    /// Raise <c>CU_FUNC_ATTRIBUTE_MAX_DYNAMIC_SHARED_SIZE_BYTES</c> to the opt-in limit of the device.
    /// </summary>
    RaiseDynamicLimit,

    /// <summary>Set <c>CU_FUNC_ATTRIBUTE_SHARED_MEMORY_MODE</c> to <c>ALLOW_OVERSIZED_SHARED_MEMORY</c>.</summary>
    AllowOversized,

    /// <summary>The device cannot hold the request; throw before the launch.</summary>
    Reject
}

/// <summary>
/// The shared memory state of one function on its device, as <see cref="SharedMemoryOptInPlan.Decide"/> reads it.
/// </summary>
/// <param name="StaticBytes">The static shared memory of the function.</param>
/// <param name="AllowedDynamicBytes">The dynamic shared memory the function allows now (function attribute 8).</param>
/// <param name="OptInPerBlock">The opt-in shared memory per block of the device (device attribute 97).</param>
/// <param name="OversizedPerBlock">
/// The oversized shared memory per block of the device (device attribute 150), or 0 when the query failed. A value
/// not above <paramref name="OptInPerBlock"/> means the device has no oversized configuration.
/// </param>
/// <param name="ModeIsSet">Whether the function's shared memory mode already allows oversized shared memory.</param>
internal readonly record struct SharedMemoryFunctionState(
    int StaticBytes,
    int AllowedDynamicBytes,
    int OptInPerBlock,
    int OversizedPerBlock,
    bool ModeIsSet);

/// <summary>
/// The decision of the CUDA path for the dynamic shared memory of one launch, made without a GPU so that every case can
/// be tested; <see cref="DynamicSharedMemoryOptIn"/> carries it out.
/// </summary>
/// <param name="Action">What to do before the launch.</param>
/// <param name="LimitBytes">
/// For <see cref="SharedMemoryOptInAction.Reject"/>, the per-block limit the request exceeds.
/// </param>
/// <param name="OversizedHintBytes">
/// For <see cref="SharedMemoryOptInAction.Reject"/> of a kernel that does not allow oversized shared memory, the
/// oversized limit that would hold the request; 0 when there is none.
/// </param>
/// <remarks>
/// The oversized mode is set only for a kernel that allows it and only when the request does not fit the opt-in limit,
/// so a launch that fits makes the same driver calls whether the kernel allows it or not. The first matching rule wins:
/// <list type="number">
///   <item><description>No dynamic shared memory: nothing.</description></item>
///   <item><description>
///   The mode is not set and the request fits what the function allows: nothing.
///   </description></item>
///   <item><description>
///   Static plus dynamic fits the opt-in limit: raise the dynamic limit (nothing once the mode is set).
///   </description></item>
///   <item><description>
///   The kernel allows oversized shared memory, the device has it, and static plus dynamic fits it: set the mode
///   (nothing once it is set).
///   </description></item>
///   <item><description>Otherwise: reject.</description></item>
/// </list>
/// </remarks>
internal readonly record struct SharedMemoryOptInPlan(
    SharedMemoryOptInAction Action,
    int LimitBytes,
    int OversizedHintBytes)
{
    /// <summary><c>CU_SHARED_MEMORY_MODE_ALLOW_OVERSIZED_SHARED_MEMORY</c>.</summary>
    public const int AllowOversizedSharedMemoryMode = 3;

    private static readonly SharedMemoryOptInPlan s_none = new(SharedMemoryOptInAction.None, 0, 0);

    /// <summary>Decides what to do before a launch that requests <paramref name="requested"/> bytes.</summary>
    /// <param name="requested">The dynamic shared memory per block that the launch requests.</param>
    /// <param name="allowsOversized">Whether the kernel allows an oversized shared memory configuration.</param>
    /// <param name="state">The function and device as they are before the launch.</param>
    public static SharedMemoryOptInPlan Decide(uint requested, bool allowsOversized, in SharedMemoryFunctionState state)
    {
        if (requested == 0 || (!state.ModeIsSet && requested <= (uint)Math.Max(0, state.AllowedDynamicBytes)))
        {
            return s_none;
        }

        // long: the request is a uint and the static bytes may exceed the limit
        long dynamicLimit = (long)state.OptInPerBlock - state.StaticBytes;
        if (requested <= dynamicLimit)
        {
            return state.ModeIsSet
                ? s_none
                : new SharedMemoryOptInPlan(SharedMemoryOptInAction.RaiseDynamicLimit, 0, 0);
        }

        var hasOversized = state.OversizedPerBlock > state.OptInPerBlock;
        var fitsOversized = hasOversized && requested <= (long)state.OversizedPerBlock - state.StaticBytes;
        if (allowsOversized && fitsOversized)
        {
            return state.ModeIsSet ? s_none : new SharedMemoryOptInPlan(SharedMemoryOptInAction.AllowOversized, 0, 0);
        }

        var limit = allowsOversized && hasOversized ? state.OversizedPerBlock : state.OptInPerBlock;
        var hint = !allowsOversized && fitsOversized ? state.OversizedPerBlock : 0;
        return new SharedMemoryOptInPlan(SharedMemoryOptInAction.Reject, limit, hint);
    }
}
