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
    /// A call that a rule writes before the standard emission: a <c>Gpu</c> macro where its rule applies, StackLinq, a
    /// span, array or <c>MemoryExtensions</c> member, <c>Nullable&lt;T&gt;.GetValueOrDefault</c>.
    /// </summary>
    ClaimedByRule,

    /// <summary>
    /// A method of <c>Memory&lt;T&gt;</c>, <c>ReadOnlyMemory&lt;T&gt;</c>, <c>ArraySegment&lt;T&gt;</c> or
    /// <c>ImmutableArray&lt;T&gt;</c> that the array view of device code defines with its C# meaning
    /// (<see cref="Analysis.ArrayViewMethods"/>).
    /// </summary>
    ArrayViewMember,

    /// <summary>
    /// A <c>Gpu</c> macro written where no macro rule writes it: outside a statement of its own, or with a body that
    /// is not a lambda (GSH0003).
    /// </summary>
    MisusedMacro,

    /// <summary>A method of the project without a device definition (GSH0107).</summary>
    Unemitted,

    /// <summary>A method of another assembly that the dialect does not implement (GSH0109).</summary>
    Unmapped,

    /// <summary>
    /// <c>nameof</c> whose value stays a string on the device (GSH0002): a length or a comparison of constant strings
    /// is written as its value before the <c>nameof</c> is reached.
    /// </summary>
    NameofString,

    /// <summary>
    /// An invocation that binds to no method and that lowering did not synthesize (GSH0003): a call through a function
    /// pointer, a call bound at run time, or a node of a lowered tree that lost its binding.
    /// </summary>
    Unbound,

    /// <summary>
    /// An invocation that C# cannot bind (a name that does not exist, no applicable overload): the C# compiler reports
    /// it, so nothing is reported and nothing is emitted for it.
    /// </summary>
    NotCSharp
}

/// <summary>
/// The decision of <see cref="CallTargetClassifier.Classify"/>: the kind of the target and, for a failure, the text of
/// its diagnostic.
/// </summary>
/// <param name="Kind">What the call calls.</param>
/// <param name="Method">
/// The method in the message: <c>Type.Method</c> for GSH0107, with its parameters for GSH0109; empty for GSH0003.
/// </param>
/// <param name="Detail">
/// The reason of GSH0107, the end of the GSH0109 message (what the dialect implements instead; empty when there is
/// nothing to add), or the syntax of GSH0003; empty for GSH0002.
/// </param>
/// <param name="Dialect">The dialect that has no implementation (GSH0109).</param>
/// <param name="ReportAt">
/// Where a failure is reported when it is not the call itself: the body argument of a misused macro.
/// </param>
internal readonly record struct CallTargetPlan(
    CallTargetKind Kind,
    string Method = "",
    string Detail = "",
    string Dialect = "",
    SyntaxNode? ReportAt = null)
{
    /// <summary>Whether the call cannot be emitted and is reported.</summary>
    public bool IsFailure => Kind is CallTargetKind.Unemitted or CallTargetKind.Unmapped or
        CallTargetKind.MisusedMacro or CallTargetKind.NameofString or CallTargetKind.Unbound;

    /// <summary>The diagnostic of a failure.</summary>
    /// <exception cref="InvalidOperationException">The plan is not a failure.</exception>
    public DiagnosticDescriptor Descriptor => Kind switch
    {
        CallTargetKind.Unemitted => GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode,
        CallTargetKind.Unmapped => GSharpDiagnostics.UnmappedMethodCalledFromGpuCode,
        CallTargetKind.MisusedMacro or CallTargetKind.Unbound => GSharpDiagnostics.UnsupportedSyntax,
        CallTargetKind.NameofString => GSharpDiagnostics.StringNotSupported,
        _ => throw new InvalidOperationException($"A call target of kind {Kind} is not a failure.")
    };

    /// <summary>The arguments of the message of <see cref="Descriptor"/>.</summary>
    public object[] MessageArgs => Kind switch
    {
        CallTargetKind.Unmapped => [Method, Dialect, Detail],
        CallTargetKind.MisusedMacro or CallTargetKind.Unbound => [Detail],
        CallTargetKind.NameofString => [],
        _ => [Method, Detail]
    };
}
