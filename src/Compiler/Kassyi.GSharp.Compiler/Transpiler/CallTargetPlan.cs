namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// The decision of <see cref="CallTargetClassifier.Classify"/>: the kind of the target and, for a failure, the text of
/// its diagnostic.
/// </summary>
/// <param name="Kind">What the call calls.</param>
/// <param name="Method">
/// The method in the message: <c>Type.Method</c> for GSH0107 and GSH0054, with its parameters for GSH0109; empty for
/// GSH0003.
/// </param>
/// <param name="Detail">
/// The reason of GSH0107, the end of the GSH0109 message (what the dialect implements instead; empty when there is
/// nothing to add), or the syntax of GSH0003; empty for GSH0002.
/// </param>
/// <param name="Dialect">The dialect that has no implementation (GSH0109, GSH0054).</param>
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

    /// <summary>The diagnostic of a failure, or the warning of an intrinsic the dialect does not implement.</summary>
    /// <exception cref="InvalidOperationException">The plan is neither a failure nor such an intrinsic.</exception>
    public DiagnosticDescriptor Descriptor => Kind switch
    {
        CallTargetKind.Unemitted => GSharpDiagnostics.UnmarkedMethodCalledFromGpuCode,
        CallTargetKind.Unmapped => GSharpDiagnostics.UnmappedMethodCalledFromGpuCode,
        CallTargetKind.MisusedMacro or CallTargetKind.Unbound => GSharpDiagnostics.UnsupportedSyntax,
        CallTargetKind.NameofString => GSharpDiagnostics.StringNotSupported,
        CallTargetKind.UnsupportedIntrinsic => GSharpDiagnostics.IntrinsicNotImplementedByDialect,
        _ => throw new InvalidOperationException($"A call target of kind {Kind} is not reported.")
    };

    /// <summary>The arguments of the message of <see cref="Descriptor"/>.</summary>
    public object[] MessageArgs => Kind switch
    {
        CallTargetKind.Unmapped => [Method, Dialect, Detail],
        CallTargetKind.MisusedMacro or CallTargetKind.Unbound => [Detail],
        CallTargetKind.NameofString => [],
        CallTargetKind.UnsupportedIntrinsic => [Method, Dialect],
        _ => [Method, Detail]
    };
}
