namespace Kassyi.GSharp.Compiler.Transpiler;

/// <summary>
/// Decides whether a call of a <c>Gpu</c> macro is written by its rule, for <see cref="CallTargetClassifier"/>: a macro
/// that takes a body, and <c>Gpu.Hint.Unroll</c>, only as a statement of its own and with the body written as a lambda
/// (<c>MacroTranspiler</c> and the macro rules, and the CPU lowering, see expression statements only); the others
/// (<c>Gpu.InlinePtx</c>, <c>Gpu.Constant</c>, <c>Gpu.SyncGrid(mode)</c>) anywhere (<c>GpuMacroTranspilerRule</c>).
/// </summary>
/// <remarks>
/// [WHY] Anywhere else the call reaches the standard emission, which would write the macro's C# name; it is a form of
/// the call the macro does not take, so it is reported as unsupported syntax (GSH0003), as <c>Gpu.SyncGrid</c> reports
/// its body that is not a lambda itself (<see cref="GpuCodeConstraints.SyncGridRequiresLambda"/>).
/// </remarks>
internal static class MacroCallTarget
{
    /// <summary>The end of the GSH0003 text for a macro body that is not a lambda.</summary>
    public const string BodyMustBeLambda = "(a Gpu macro body must be a lambda)";

    /// <summary>The end of the GSH0003 text for a statement macro outside a statement of its own.</summary>
    public const string MustBeStatement = "(call it as a statement of its own)";

    /// <summary>
    /// The plan of <paramref name="call"/>, an invocation or a method group, of <paramref name="method"/>, the macro
    /// <paramref name="macro"/>.
    /// </summary>
    public static CallTargetPlan Classify(GpuMacroCall macro, IMethodSymbol method, SyntaxNode call)
    {
        var name = IteratorTypeResolver.FormatType(method.ContainingType) + "." + method.Name;
        if (call is not InvocationExpressionSyntax invocation)
        {
            return Misused(name + " used as a method group " + MustBeStatement, call);
        }

        if (!IsStatementMacro(macro))
        {
            return new CallTargetPlan(CallTargetKind.ClaimedByRule);
        }

        if (invocation.Parent is not ExpressionStatementSyntax)
        {
            return Misused(name + " outside a statement " + MustBeStatement, invocation);
        }

        // Gpu.SyncGrid reports a body that is not a lambda itself (SyncGridMacroRule, SyncGridChecks)
        var arguments = invocation.ArgumentList.Arguments;
        if (!macro.HasBody || macro.Kind == GpuMacroKind.SyncGrid || macro.GetBody(arguments) is not null)
        {
            return new CallTargetPlan(CallTargetKind.ClaimedByRule);
        }

        var body = macro.GetArgument(arguments, macro.BodyOrdinal);
        return Misused(name + " with a body that is not a lambda " + BodyMustBeLambda, (SyntaxNode?)body ?? invocation);
    }

    // The macros that only MacroTranspiler writes, at an expression statement
    private static bool IsStatementMacro(GpuMacroCall macro) =>
        macro.HasBody || macro.Kind == GpuMacroKind.HintUnroll;

    private static CallTargetPlan Misused(string syntax, SyntaxNode reportAt) =>
        new(CallTargetKind.MisusedMacro, Detail: syntax, ReportAt: reportAt);
}
