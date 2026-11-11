namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>What a call in GPU code, or a method group it names, calls on the device.</summary>
internal enum CallTargetKind
{
    /// <summary>
    /// C# removes the call: a partial method without an implementation, or a <c>[Conditional]</c> one.
    /// </summary>
    Removed,

    /// <summary>A call that lowering synthesized, which has no written call to decide.</summary>
    Synthesized,

    /// <summary>Not an ordinary method: a delegate's <c>Invoke</c>, a local function, an accessor.</summary>
    NotOrdinary,

    /// <summary>
    /// A <c>[GpuDeviceFunction]</c> or <c>[GpuKernel]</c> method, of the project or of another assembly.
    /// </summary>
    DeviceFunction,

    /// <summary>
    /// A native function of the dialect: <c>[CudaIntrinsic]</c>, an intrinsic pattern, <c>[CudaImport]</c>, or a row
    /// of the dialect's YAML.
    /// </summary>
    DialectIntrinsic,

    /// <summary>A method that <c>[BclIntrinsicMap]</c> maps.</summary>
    BclIntrinsicMap,

    /// <summary>A <c>[GpuOperatorMacro]</c> method.</summary>
    OperatorMacro,

    /// <summary>
    /// A call that a rule writes before the standard emission: a <c>Gpu</c> macro, StackLinq, a span, array or
    /// <c>MemoryExtensions</c> member, <c>Nullable&lt;T&gt;.GetValueOrDefault</c>.
    /// </summary>
    ClaimedByRule,

    /// <summary>A method of the project without a device definition (GSH0107).</summary>
    Unemitted,

    /// <summary>A method of another assembly that the dialect does not implement (GSH0109).</summary>
    Unmapped
}

/// <summary>
/// The decision of <see cref="CallTargetClassifier.Classify"/>: the kind of the target and, for a failure, the text of
/// its diagnostic.
/// </summary>
/// <param name="Kind">What the call calls.</param>
/// <param name="Method">
/// The method in the message: <c>Type.Method</c> for GSH0107, with its parameters for GSH0109.
/// </param>
/// <param name="Detail">
/// The reason of GSH0107, or the end of the GSH0109 message: what the dialect implements instead, or why a macro is not
/// written; empty when there is nothing to add.
/// </param>
/// <param name="Dialect">The dialect that has no implementation (GSH0109).</param>
internal readonly record struct CallTargetPlan(
    CallTargetKind Kind,
    string Method = "",
    string Detail = "",
    string Dialect = "")
{
    /// <summary>Whether the call cannot be emitted and is reported.</summary>
    public bool IsFailure => Kind is CallTargetKind.Unemitted or CallTargetKind.Unmapped;

    /// <summary>The diagnostic of a failure.</summary>
    /// <exception cref="InvalidOperationException">The plan is not a failure.</exception>
    public DiagnosticDescriptor Descriptor => Kind switch
    {
        CallTargetKind.Unemitted => GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode,
        CallTargetKind.Unmapped => GSharpDiagnostics.UnmappedMethodCalledFromGpuCode,
        _ => throw new InvalidOperationException($"A call target of kind {Kind} is not a failure.")
    };

    /// <summary>The arguments of the message of <see cref="Descriptor"/>.</summary>
    public object[] MessageArgs => Kind == CallTargetKind.Unmapped ? [Method, Dialect, Detail] : [Method, Detail];
}
