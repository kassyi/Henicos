namespace Kassyi.GSharp.Compiler.Analysis;

internal enum NativeStackObstacle
{
    None,
    NoBlockBarrier,
    GridSync,
    Cooperative,
    RecursiveSuspendingCall,

    /// <summary>
    /// A suspending device function from another assembly has no usable native stack C++ form
    /// (<see cref="Models.DeviceExportRecord.HasCppStack"/> is false, or the record is incompatible).
    /// </summary>
    ImportedCallWithoutStackForm,

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
    WarpOperatorMacro,

    /// <summary>
    /// A suspending device function that reaches a warp collective is passed as a method group: the native stack form
    /// of the target takes a warp frame offset (<c>__gsharp_warp_frame</c>), which a delegate cannot pass, and
    /// <see cref="WarpFrameLayout"/> only places calls written as calls.
    /// </summary>
    WarpMethodGroup,

    /// <summary>
    /// The body of a method that reaches <c>Warp.ActiveMask()</c> could not be lowered for the stacks
    /// (<c>AstLoweringPipeline.LowerForStack</c> failed): the method keeps its state machine, as on the managed stack
    /// model, instead of the stack form failing the native build.
    /// </summary>
    StackBodyLowering,

    /// <summary>
    /// A barrier or warp collective reached only through a struct member, which has no stack form yet.
    /// </summary>
    StructMemberSplit
}
