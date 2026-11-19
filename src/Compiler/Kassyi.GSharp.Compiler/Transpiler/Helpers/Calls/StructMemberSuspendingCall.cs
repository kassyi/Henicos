namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

/// <summary>
/// Records GSH0062 when the body of a struct member that the transpiler writes (a constructor and the member
/// initializers it runs, a property accessor, a conversion or truth operator, <c>Dispose</c>, a sort comparison member)
/// calls a <c>[GpuDeviceFunction]</c> that reaches a barrier or warp shuffle.
/// </summary>
/// <remarks>
/// [WHY] Only kernel and device function bodies are lowered into the CPU state machine; a struct member is written as a
/// plain function, so the call could not suspend and its frame would be missing (the native build failed with an
/// MSVC error, #1119). The check sits where the body is written, not in the analyzer, so that only the members that
/// device code runs are reported: a struct that no device code uses, and a member that only the host calls (a truth
/// operator without an operator macro, a <c>Dispose</c> that no device <c>using</c> calls), are never written. The
/// call is reported for every dialect, as a call of such a function in a lambda is (BarrierUsageAnalyzer).
/// </remarks>
internal static class StructMemberSuspendingCall
{
    /// <summary>
    /// Reports GSH0062 at <paramref name="invocation"/> when a struct member body is being written
    /// (<see cref="Emitter.CurrentStructType"/>) and <paramref name="method"/> reaches a barrier or warp shuffle.
    /// </summary>
    public static void FailIfInStructMember(InvocationExpressionSyntax invocation, IMethodSymbol method,
        ref TranspileContext context)
    {
        var model = context.Query.SemanticModel;
        if (context.Emitter.CurrentStructType == null ||
            !SuspensionAnalysis.IsSuspendingFunction(method, model.Compilation))
        {
            return;
        }

        context.Reporter.Fail(GSharpDiagnostics.SuspendingCallInUnsupportedContext, invocation, method.Name,
            DescribeMember(invocation, model), SplitPointPlacement.SuspendingDeviceFunctionCallee);
    }

    // The member whose body holds the call; a lambda or local function belongs to the member that declares it
    private static string DescribeMember(SyntaxNode invocation, SemanticModel model)
    {
        var member = invocation.SyntaxTree == model.SyntaxTree
            ? model.GetEnclosingSymbol(invocation.SpanStart)
            : null;
        while (member is IMethodSymbol { MethodKind: MethodKind.AnonymousFunction or MethodKind.LocalFunction })
        {
            member = member.ContainingSymbol;
        }

        return member switch
        {
            IMethodSymbol { MethodKind: MethodKind.Constructor } => "in a constructor",
            IMethodSymbol { MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet } => "in a property accessor",
            IMethodSymbol { MethodKind: MethodKind.Conversion } => "in a conversion operator",
            IMethodSymbol { MethodKind: MethodKind.UserDefinedOperator } => "in a user-defined operator",
            // A member initializer runs as part of the constructor
            IFieldSymbol or IPropertySymbol => "in a constructor",
            _ => "in a method of a struct"
        };
    }
}
