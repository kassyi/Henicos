namespace Kassyi.GSharp.Compiler.Transpiler.Helpers.Calls;

/// <summary>
/// Records GSH0062 when a <c>[GpuDeviceFunction]</c> that reaches a barrier or warp shuffle is referenced without
/// calling it (a method group or delegate), outside a <c>Gpu</c> macro body argument (which already reports GSH0003).
/// </summary>
/// <remarks>
/// A call of such a function in the reduced extension form is GSH0003
/// (<c>InvocationExpressionTranspilerRule.FailIfSuspendingExtension</c>): the call cannot be written in the static
/// form that passes the frame. A reference without a call, reduced extension method groups included, is GSH0062: the
/// delegate would reach the barrier from a place the CPU state machine cannot split. The two checks differ on purpose.
/// </remarks>
internal static class SuspendingCallReference
{
    /// <summary>
    /// Reports GSH0062 and returns <see langword="true"/> when <paramref name="reference"/> names
    /// <paramref name="method"/> without calling it, is not a <c>Gpu</c> macro body argument, and
    /// <paramref name="method"/> reaches a barrier or warp shuffle.
    /// </summary>
    public static bool FailIfReferencedWithoutCall(
        ExpressionSyntax reference,
        IMethodSymbol method,
        ref TranspileContext context)
    {
        if (CallTargetClassifier.IsCalledReference(reference) ||
            GpuMacroClassifier.ClassifyBody(reference, context.Query.SemanticModel).HasBody ||
            !SuspensionAnalysis.IsSuspendingFunction(method, context.Query.SemanticModel.Compilation))
        {
            return false;
        }

        context.Reporter.Fail(
            GSharpDiagnostics.SuspendingCallInUnsupportedContext,
            reference,
            method.Name,
            "through a delegate",
            SplitPointPlacement.SuspendingDeviceFunctionCallee);
        return true;
    }
}
