namespace Kassyi.GSharp.Compiler.Analysis;

internal enum NativeStackObstacle
{
    None,
    NoBlockBarrier,
    GridSync,
    Cooperative,
    RecursiveSuspendingCall,
    ImportedSuspendingCall,

    /// <summary>
    /// The kernel reaches <c>Warp.ActiveMask()</c>, whose reconvergence regions the native stack model does not place
    /// yet.
    /// </summary>
    WarpActiveMask,

    /// <summary>
    /// A warp collective or a call that reaches one has no place in the visit counts of the kernel
    /// (<see cref="WarpFrameLayout.TryGet"/> fails).
    /// </summary>
    WarpLayout,

    /// <summary>
    /// The visit counts of the kernel's warp collectives are more than <see cref="WarpFrameLayout.MaxSize"/>, which each
    /// GPU thread would keep on its own stack.
    /// </summary>
    WarpFrameTooLarge,

    /// <summary>
    /// A <c>case</c> value that names no C++ label (<c>case float.NaN:</c>): only the state machine, which flattens the
    /// switch at a split point, writes it.
    /// </summary>
    CaseValueWithoutLabel,

    /// <summary>
    /// An operator macro (<c>[GpuOperatorMacro]</c>) whose target reaches a warp collective: its expansion is a call that
    /// <see cref="WarpFrameLayout"/> does not place, so it would have no frame on the stacks.
    /// </summary>
    WarpOperatorMacro
}
